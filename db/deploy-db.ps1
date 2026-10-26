<#
.SYNOPSIS
    Aplica os scripts de banco pendentes do PortalVendas, em ordem, registrando cada um em dbo.SchemaVersao.

.EXAMPLE
    # Desenvolvimento (autenticação do Windows, cria o banco e carrega os dados de exemplo)
    .\deploy-db.ps1 -Servidor "localhost" -Banco "PortalVendas" -CriarBanco -ComSeed

.EXAMPLE
    # Produção (usuário SQL; a senha é pedida de forma segura)
    .\deploy-db.ps1 -Servidor "SRVSQL01" -Banco "PortalVendas" -Usuario "deploy_portal"

.NOTES
    Cada script roda dentro de uma transação: se falhar, nada daquele script fica aplicado.
    Faça backup do banco de produção antes de rodar (ver docs/06-implantacao.md).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Servidor,
    [Parameter(Mandatory)] [string] $Banco,
    [string] $Usuario,
    [System.Management.Automation.PSCredential] $Credencial,
    [switch] $CriarBanco,
    [switch] $ComSeed,
    [switch] $SomenteListar
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data

$cred = $Credencial
if (-not $cred -and $Usuario) { $cred = Get-Credential -UserName $Usuario -Message "Senha do usuário SQL $Usuario" }

function New-ConnectionString([string] $database) {
    $b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $b['Data Source'] = $Servidor
    $b['Initial Catalog'] = $database
    $b['Application Name'] = 'PortalVendas.deploy-db'
    if ($Servidor -like '(localdb)*') {
        # LocalDB é local (sem rede) e algumas versões não suportam criptografia
        $b['Encrypt'] = $false
    } else {
        $b['Encrypt'] = $true
        $b['TrustServerCertificate'] = $true   # troque para $false se o servidor tiver certificado válido
    }
    if ($cred) {
        $b['User ID'] = $cred.UserName
        $b['Password'] = $cred.GetNetworkCredential().Password
    } else {
        $b['Integrated Security'] = $true
    }
    return $b.ConnectionString
}

function Invoke-Batches([System.Data.SqlClient.SqlConnection] $conn, [System.Data.SqlClient.SqlTransaction] $tran, [string] $sql) {
    # Divide nos separadores GO (linha contendo só "GO")
    $batches = [regex]::Split($sql, '^\s*GO\s*;?\s*$', [System.Text.RegularExpressions.RegexOptions]::Multiline -bor [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    foreach ($batch in $batches) {
        if ([string]::IsNullOrWhiteSpace($batch)) { continue }
        $cmd = $conn.CreateCommand()
        $cmd.Transaction = $tran
        $cmd.CommandTimeout = 600
        $cmd.CommandText = $batch
        [void] $cmd.ExecuteNonQuery()
    }
}

$connStr = New-ConnectionString $Banco

if ($CriarBanco) {
    $master = New-Object System.Data.SqlClient.SqlConnection((New-ConnectionString 'master'))
    $master.Open()
    try {
        $cmd = $master.CreateCommand()
        $cmd.CommandText = "IF DB_ID(@nome) IS NULL BEGIN DECLARE @sql NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@nome); EXEC (@sql); END"
        [void] $cmd.Parameters.AddWithValue('@nome', $Banco)
        [void] $cmd.ExecuteNonQuery()
        Write-Host "Banco '$Banco' verificado/criado." -ForegroundColor Green
    } finally { $master.Dispose() }
}

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()
try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @"
IF OBJECT_ID('dbo.SchemaVersao') IS NULL
CREATE TABLE dbo.SchemaVersao (
    Versao     VARCHAR(20)   NOT NULL CONSTRAINT PK_SchemaVersao PRIMARY KEY,
    Script     NVARCHAR(260) NOT NULL,
    AplicadoEm DATETIME2(0)  NOT NULL CONSTRAINT DF_SchemaVersao_AplicadoEm DEFAULT (SYSUTCDATETIME()),
    AplicadoPor NVARCHAR(128) NOT NULL CONSTRAINT DF_SchemaVersao_AplicadoPor DEFAULT (SUSER_SNAME())
);
"@
    [void] $cmd.ExecuteNonQuery()

    $aplicadas = @{}
    $cmd.CommandText = 'SELECT Versao FROM dbo.SchemaVersao'
    $r = $cmd.ExecuteReader()
    while ($r.Read()) { $aplicadas[$r.GetString(0)] = $true }
    $r.Close()

    $pastas = @(Join-Path $PSScriptRoot 'migrations')
    if ($ComSeed) { $pastas += (Join-Path $PSScriptRoot 'seed') }

    $scripts = foreach ($p in $pastas) { Get-ChildItem -Path $p -Filter '*.sql' | Sort-Object Name }
    $pendentes = $scripts | Where-Object { -not $aplicadas.ContainsKey(($_.BaseName -split '__')[0]) }

    if (-not $pendentes) { Write-Host 'Nenhum script pendente. Banco atualizado.' -ForegroundColor Green; return }

    Write-Host 'Scripts pendentes:' -ForegroundColor Cyan
    $pendentes | ForEach-Object { Write-Host "  $($_.Name)" }
    if ($SomenteListar) { return }

    foreach ($s in $pendentes) {
        $versao = ($s.BaseName -split '__')[0]
        Write-Host "Aplicando $($s.Name)..." -NoNewline
        $tran = $conn.BeginTransaction()
        try {
            Invoke-Batches $conn $tran ([System.IO.File]::ReadAllText($s.FullName, [System.Text.Encoding]::UTF8))
            $ins = $conn.CreateCommand()
            $ins.Transaction = $tran
            $ins.CommandText = 'INSERT INTO dbo.SchemaVersao (Versao, Script) VALUES (@v, @s)'
            [void] $ins.Parameters.AddWithValue('@v', $versao)
            [void] $ins.Parameters.AddWithValue('@s', $s.Name)
            [void] $ins.ExecuteNonQuery()
            $tran.Commit()
            Write-Host ' OK' -ForegroundColor Green
        } catch {
            $tran.Rollback()
            Write-Host ' FALHOU' -ForegroundColor Red
            throw "Erro em $($s.Name): $($_.Exception.Message)"
        }
    }
    Write-Host 'Banco atualizado com sucesso.' -ForegroundColor Green
} finally {
    $conn.Dispose()
}
