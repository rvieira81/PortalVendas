<#
.SYNOPSIS
    Publica uma nova versão do PortalVendas no IIS com backup e rollback.

.DESCRIPTION
    1. Roda os testes e publica em Release.
    2. Faz backup da versão atual do site (pasta zipada com data/hora).
    3. Coloca o site em manutenção (app_offline.htm), copia os arquivos e retira a manutenção.
    4. Confere o /health. Se falhar, restaura o backup automaticamente.
    O banco é atualizado antes, com db\deploy-db.ps1 (após backup do banco).

.EXAMPLE
    .\publicar.ps1 -PastaSite "D:\Sites\PortalVendas" -UrlSite "https://www.sualoja.com.br"

.EXAMPLE
    # Voltar para uma versão anterior
    .\publicar.ps1 -PastaSite "D:\Sites\PortalVendas" -UrlSite "https://www.sualoja.com.br" -RestaurarBackup "D:\Backups\PortalVendas\site-20261020-2200.zip"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PastaSite,
    [Parameter(Mandatory)] [string] $UrlSite,
    [string] $PastaBackups = 'D:\Backups\PortalVendas',
    [string] $RestaurarBackup,
    [switch] $PularTestes
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$publicacao = Join-Path $raiz 'publish'
$preservar = @('appsettings.Production.json', 'logs')   # nunca sobrescritos no servidor

function Entrar-Manutencao {
    $html = '<!doctype html><html lang="pt-BR"><meta charset="utf-8"><title>Em manutenção</title><body style="font-family:sans-serif;text-align:center;padding:60px"><h1>Voltamos em instantes</h1><p>Estamos atualizando a loja.</p></body></html>'
    Set-Content -Path (Join-Path $PastaSite 'app_offline.htm') -Value $html -Encoding UTF8
    Start-Sleep -Seconds 3   # o ASP.NET Core Module encerra o processo ao ver o arquivo
}

function Sair-Manutencao { Remove-Item (Join-Path $PastaSite 'app_offline.htm') -ErrorAction SilentlyContinue }

function Testar-Saude {
    for ($i = 1; $i -le 10; $i++) {
        try {
            $r = Invoke-WebRequest -Uri "$UrlSite/health" -UseBasicParsing -TimeoutSec 15
            if ($r.StatusCode -eq 200) { return $true }
        } catch { Start-Sleep -Seconds 3 }
    }
    return $false
}

function Restaurar([string] $zip) {
    Write-Host "Restaurando $zip..." -ForegroundColor Yellow
    Entrar-Manutencao
    Get-ChildItem $PastaSite -Exclude ($preservar + 'app_offline.htm') | Remove-Item -Recurse -Force
    Expand-Archive -Path $zip -DestinationPath $PastaSite -Force
    Sair-Manutencao
}

if ($RestaurarBackup) {
    Restaurar $RestaurarBackup
    if (Testar-Saude) { Write-Host 'Versão anterior restaurada e respondendo.' -ForegroundColor Green } else { throw 'Restaurado, mas /health não respondeu.' }
    return
}

# ------------------------------------------------------------------ build
Push-Location $raiz
try {
    if (-not $PularTestes) {
        dotnet test PortalVendas.sln -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Testes falharam. Publicação cancelada.' }
    }
    if (Test-Path $publicacao) { Remove-Item $publicacao -Recurse -Force }
    dotnet publish src\PortalVendas.Web\PortalVendas.Web.csproj -c Release -o $publicacao
    if ($LASTEXITCODE -ne 0) { throw 'Falha no dotnet publish.' }
} finally { Pop-Location }

# ------------------------------------------------------------------ backup da versão atual
if (-not (Test-Path $PastaBackups)) { New-Item -ItemType Directory -Path $PastaBackups | Out-Null }
$backup = Join-Path $PastaBackups ("site-{0:yyyyMMdd-HHmm}.zip" -f (Get-Date))
$atuais = Get-ChildItem $PastaSite -Exclude ($preservar + 'app_offline.htm')
if ($atuais) {
    Compress-Archive -Path $atuais.FullName -DestinationPath $backup -Force
    Write-Host "Backup: $backup"
}

# ------------------------------------------------------------------ cópia
Entrar-Manutencao
try {
    robocopy $publicacao $PastaSite /MIR /XF app_offline.htm appsettings.Production.json /XD logs /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy falhou (código $LASTEXITCODE)." }
} finally {
    Sair-Manutencao
}

# ------------------------------------------------------------------ verificação
if (Testar-Saude) {
    Write-Host 'Publicado com sucesso. /health OK.' -ForegroundColor Green
} elseif ($atuais) {
    Write-Host '/health falhou após a publicação. Revertendo...' -ForegroundColor Red
    Restaurar $backup
    throw 'Publicação revertida. Veja os logs em D:\PortalVendas\logs.'
} else {
    throw '/health falhou e não há versão anterior para restaurar.'
}
