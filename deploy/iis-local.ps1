<#
.SYNOPSIS
    Publica e roda o PortalVendas no IIS desta máquina: https://localhost:8443 (execute como Administrador).

.DESCRIPTION
    Na primeira vez prepara tudo; nas próximas só republica (cerca de 30 s):
      1. Confere IIS, ASP.NET Core Hosting Bundle e o SQL Server.
      2. Aplica os scripts de banco pendentes (db\deploy-db.ps1). Com -CriarBanco cria o banco com dados de exemplo.
      3. Cria o Application Pool e o site "PortalVendasLocal" com certificado HTTPS confiável para localhost.
      4. Publica o projeto (Debug por padrão, para poder anexar o depurador) com o ambiente "Local".
      5. Gera appsettings.Local.json (banco, pastas, URL) + os user-secrets do projeto (Mercado Pago, Melhor Envio...).
      6. Dá ao pool acesso às pastas e ao banco (login "IIS AppPool\PortalVendasLocal").
      7. Sobe o site, confere o /health e abre o navegador.

    O IIS NÃO consegue usar o LocalDB (ele é por usuário do Windows): use uma instância do SQL Server
    (localhost, localhost\SQLEXPRESS...). Ver docs\10-ambiente-local.md.

.EXAMPLE
    .\deploy\iis-local.ps1 -CriarBanco                         # primeira vez, banco novo com dados de exemplo
.EXAMPLE
    .\deploy\iis-local.ps1                                     # republicar depois de alterar o código
.EXAMPLE
    .\deploy\iis-local.ps1 -Servidor "localhost\SQLEXPRESS"    # outra instância do SQL Server
.EXAMPLE
    .\deploy\iis-local.ps1 -CriarAdmin "vieiraprado@hotmail.com"  # cria o administrador no banco do IIS (pede a senha)
.EXAMPLE
    .\deploy\iis-local.ps1 -Remover                            # remove o site e o pool (mantém banco e pastas de dados)
#>
[CmdletBinding()]
param(
    [string] $Servidor = 'localhost',
    [string] $Banco = 'PortalVendas',
    [int] $PortaHttps = 8443,
    [int] $PortaHttp = 8080,
    [string] $PastaSite = 'C:\inetpub\PortalVendasLocal',
    [string] $PastaDados = 'C:\PortalVendasLocal',
    [ValidateSet('Debug', 'Release')] [string] $Configuracao = 'Debug',
    [switch] $CriarBanco,
    [string] $CriarAdmin,
    [switch] $NaoAbrirNavegador,
    [switch] $Remover
)

$ErrorActionPreference = 'Stop'
$NomeSite = 'PortalVendasLocal'
$NomePool = 'PortalVendasLocal'
$IdentidadePool = "IIS AppPool\$NomePool"
$raiz = Split-Path $PSScriptRoot -Parent
$projeto = Join-Path $raiz 'src\PortalVendas.Web'
$url = "https://localhost:$PortaHttps"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Passo([string] $texto) { Write-Host "`n==> $texto" -ForegroundColor Cyan }
function Ok([string] $texto) { Write-Host "    $texto" -ForegroundColor Green }
function Falhar([string] $texto) { Write-Host "`n$texto" -ForegroundColor Red; exit 1 }

# ------------------------------------------------------------------ pré-requisitos
$eu = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $eu.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Falhar 'Execute como Administrador (PowerShell, rodar-iis-local.cmd ou Visual Studio aberto como administrador).'
}
if (-not (Get-Module -ListAvailable -Name WebAdministration)) {
    Falhar 'IIS não encontrado. Ative em "Ativar ou desativar recursos do Windows" > Serviços de Informações da Internet (inclua "Ferramentas de Gerenciamento da Web").'
}
Import-Module WebAdministration

if ($Remover) {
    Passo "Removendo o site $NomeSite"
    if (Test-Path "IIS:\Sites\$NomeSite") { Remove-Website -Name $NomeSite; Ok 'Site removido.' }
    if (Test-Path "IIS:\AppPools\$NomePool") { Remove-WebAppPool -Name $NomePool; Ok 'Application Pool removido.' }
    & netsh http delete sslcert ipport=0.0.0.0:$PortaHttps 2>$null | Out-Null
    Write-Host "`nBanco '$Banco' e a pasta $PastaDados foram mantidos." -ForegroundColor Yellow
    exit 0
}

# O módulo V2 fica em "C:\Program Files\IIS\Asp.Net Core Module\V2" e é registrado como módulo global do IIS
if (-not (Get-WebGlobalModule -Name 'AspNetCoreModuleV2' -ErrorAction SilentlyContinue)) {
    Falhar ("Falta o ASP.NET Core Hosting Bundle (.NET 10).`n" +
            "Baixe em https://dotnet.microsoft.com/download/dotnet/10.0 (seção ASP.NET Core Runtime > Hosting Bundle),`n" +
            "instale e rode 'iisreset' antes de executar este script de novo.")
}
if ($Servidor -like '(localdb)*') {
    Falhar ("O IIS não acessa o LocalDB (ele pertence ao seu usuário do Windows).`n" +
            "Use uma instância do SQL Server: -Servidor localhost ou -Servidor localhost\SQLEXPRESS.`n" +
            "Para levar os dados do LocalDB, veja docs\10-ambiente-local.md (backup e restore).")
}

# ------------------------------------------------------------------ banco
Passo "Banco '$Banco' em '$Servidor'"
try {
    $teste = New-Object System.Data.SqlClient.SqlConnection("Server=$Servidor;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=8")
    $teste.Open(); $teste.Dispose()
} catch {
    Falhar ("Não consegui conectar no SQL Server '$Servidor': $($_.Exception.Message)`n" +
            "Confira o nome da instância no SSMS (ex.: localhost\SQLEXPRESS) e use -Servidor. Sem SQL Server instalado,`n" +
            "instale o SQL Server 2022 Developer (gratuito) - veja docs\10-ambiente-local.md.")
}
$deployDb = Join-Path $raiz 'db\deploy-db.ps1'
if ($CriarBanco) { & $deployDb -Servidor $Servidor -Banco $Banco -CriarBanco -ComSeed }
else { & $deployDb -Servidor $Servidor -Banco $Banco }

# ------------------------------------------------------------------ pastas
Passo 'Pastas'
foreach ($p in @($PastaSite, "$PastaSite\logs", $PastaDados, "$PastaDados\imagens", "$PastaDados\chaves", "$PastaDados\emails")) {
    if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p | Out-Null }
}
# Fotos dos produtos de exemplo e licença do projeto (só se ainda não existirem no destino)
$imagensProjeto = Join-Path $projeto 'imagens'
if ((Test-Path $imagensProjeto) -and -not (Get-ChildItem "$PastaDados\imagens" -File | Select-Object -First 1)) {
    Copy-Item "$imagensProjeto\*" "$PastaDados\imagens" -Recurse
    Ok 'Fotos dos produtos copiadas.'
}
# Chaves do Data Protection do projeto: com elas o site do IIS lê o que foi cifrado rodando pelo Visual Studio
# (código do autenticador do admin, tokens do WhatsApp) quando você leva o banco do LocalDB para cá
$chavesProjeto = Join-Path $projeto 'keys'
if ((Test-Path $chavesProjeto) -and -not (Get-ChildItem "$PastaDados\chaves" -File | Select-Object -First 1)) {
    Copy-Item "$chavesProjeto\*" "$PastaDados\chaves"
    Ok 'Chaves de criptografia copiadas do projeto.'
}
$licencaProjeto = Join-Path $projeto 'licenca.lic'
if ((Test-Path $licencaProjeto) -and -not (Test-Path "$PastaDados\licenca.lic")) {
    Copy-Item $licencaProjeto "$PastaDados\licenca.lic"
    Ok 'Licença copiada do projeto.'
}
Ok "Site: $PastaSite   Dados: $PastaDados"

# ------------------------------------------------------------------ Application Pool e certificado
Passo 'IIS'
$pool = "IIS:\AppPools\$NomePool"
if (-not (Test-Path $pool)) { New-WebAppPool -Name $NomePool | Out-Null; Ok "Application Pool $NomePool criado." }
Set-ItemProperty $pool -Name managedRuntimeVersion -Value ''                    # No Managed Code
Set-ItemProperty $pool -Name processModel.loadUserProfile -Value $true          # Data Protection (DPAPI)
Set-ItemProperty $pool -Name startMode -Value 'AlwaysRunning'                   # jobs em segundo plano (manutenção, WhatsApp)
Set-ItemProperty $pool -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)

$cert = Get-ChildItem Cert:\LocalMachine\My |
    Where-Object { $_.FriendlyName -eq 'PortalVendas Local' -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
    Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -DnsName 'localhost' -CertStoreLocation 'Cert:\LocalMachine\My' `
        -FriendlyName 'PortalVendas Local' -NotAfter (Get-Date).AddYears(3)
    # Confia no certificado nesta máquina (o navegador não mostra alerta)
    $raizes = New-Object System.Security.Cryptography.X509Certificates.X509Store('Root', 'LocalMachine')
    $raizes.Open('ReadWrite'); $raizes.Add($cert); $raizes.Close()
    Ok 'Certificado HTTPS para localhost criado e marcado como confiável.'
}

if (-not (Test-Path "IIS:\Sites\$NomeSite")) {
    $ocupada = Get-WebBinding | Where-Object { $_.bindingInformation -like "*:${PortaHttps}:*" -or $_.bindingInformation -like "*:${PortaHttp}:*" }
    if ($ocupada) { Falhar "As portas $PortaHttps/$PortaHttp já estão em uso por outro site do IIS. Use -PortaHttps e -PortaHttp." }
    New-Website -Name $NomeSite -PhysicalPath $PastaSite -ApplicationPool $NomePool -Port $PortaHttps -Ssl | Out-Null
    New-WebBinding -Name $NomeSite -Protocol http -Port $PortaHttp
    Ok "Site $NomeSite criado ($url)."
}
& netsh http delete sslcert ipport=0.0.0.0:$PortaHttps 2>$null | Out-Null
(Get-WebBinding -Name $NomeSite -Protocol https).AddSslCertificate($cert.Thumbprint, 'My')

# ------------------------------------------------------------------ publicação
Passo "Publicando ($Configuracao)"
if ((Get-WebAppPoolState -Name $NomePool).Value -eq 'Started') { Stop-WebAppPool -Name $NomePool; Start-Sleep -Seconds 2 }
& dotnet publish $projeto -c $Configuracao -o $PastaSite /p:EnvironmentName=Local --nologo -v minimal
if ($LASTEXITCODE -ne 0) { Falhar 'Falha no dotnet publish (veja os erros acima).' }

# appsettings.Local.json: o que muda no IIS + os user-secrets do projeto (que o IIS não enxerga)
function Definir($raizConfig, [string] $chave, $valor) {
    $partes = $chave.Split(':')
    $atual = $raizConfig
    for ($i = 0; $i -lt $partes.Length - 1; $i++) {
        if (-not $atual.Contains($partes[$i])) { $atual[$partes[$i]] = [ordered]@{} }
        $atual = $atual[$partes[$i]]
    }
    $atual[$partes[$partes.Length - 1]] = $valor
}
$config = [ordered]@{}
$segredos = & dotnet user-secrets list --project $projeto 2>$null
foreach ($linha in $segredos) {
    if ($linha -match '^(.+?) = (.*)$' -and $Matches[1] -notlike 'ConnectionStrings:*') { Definir $config $Matches[1] $Matches[2] }
}
Definir $config 'ConnectionStrings:PortalVendas' "Server=$Servidor;Database=$Banco;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Application Name=PortalVendas.IIS"
Definir $config 'Loja:UrlBase' $url
Definir $config 'Licenca:Arquivo' "$PastaDados\licenca.lic"
Definir $config 'Imagens:Pasta' "$PastaDados\imagens"
Definir $config 'DataProtection:PastaChaves' "$PastaDados\chaves"
Definir $config 'Smtp:PastaArquivos' "$PastaDados\emails"
$config | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $PastaSite 'appsettings.Local.json') -Encoding UTF8
Ok 'appsettings.Local.json gerado (inclui os user-secrets; a pasta só é acessível a administradores e ao pool).'

# ------------------------------------------------------------------ permissões
Passo 'Permissões'
# Sem herança: o appsettings.Local.json tem segredos (só Administradores, SYSTEM e o pool leem). SIDs servem em Windows de qualquer idioma.
icacls $PastaSite /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${IdentidadePool}:(OI)(CI)RX" /Q | Out-Null
icacls "$PastaSite\logs" /grant "${IdentidadePool}:(OI)(CI)M" /Q | Out-Null
icacls $PastaDados /grant "${IdentidadePool}:(OI)(CI)M" /Q | Out-Null
Ok "$IdentidadePool pode ler o site e gravar em logs e em $PastaDados."

$sqlLogin = @"
IF SUSER_ID(N'$IdentidadePool') IS NULL CREATE LOGIN [$IdentidadePool] FROM WINDOWS;
USE [$Banco];
IF USER_ID(N'$IdentidadePool') IS NULL CREATE USER [$IdentidadePool] FOR LOGIN [$IdentidadePool];
ALTER ROLE db_datareader ADD MEMBER [$IdentidadePool];
ALTER ROLE db_datawriter ADD MEMBER [$IdentidadePool];
GRANT EXECUTE TO [$IdentidadePool];
"@
$cn = New-Object System.Data.SqlClient.SqlConnection("Server=$Servidor;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
$cn.Open()
try { $cmd = $cn.CreateCommand(); $cmd.CommandText = $sqlLogin; [void] $cmd.ExecuteNonQuery() } finally { $cn.Dispose() }
Ok "Login '$IdentidadePool' com leitura e gravação no banco $Banco."

# ------------------------------------------------------------------ administrador (opcional)
if ($CriarAdmin) {
    Passo "Criando o administrador $CriarAdmin (digite a senha quando pedir)"
    $env:ASPNETCORE_ENVIRONMENT = 'Local'
    Push-Location $PastaSite
    try { & dotnet .\PortalVendas.Web.dll criar-admin $CriarAdmin 'Administrador' } finally { Pop-Location; Remove-Item Env:\ASPNETCORE_ENVIRONMENT }
}

# ------------------------------------------------------------------ subir e conferir
Passo 'Subindo o site'
Start-WebAppPool -Name $NomePool -ErrorAction SilentlyContinue
if ((Get-WebsiteState -Name $NomeSite).Value -ne 'Started') { Start-Website -Name $NomeSite }
$saudavel = $false
for ($i = 1; $i -le 15 -and -not $saudavel; $i++) {
    try { $saudavel = (Invoke-WebRequest -Uri "$url/health" -UseBasicParsing -TimeoutSec 10).StatusCode -eq 200 }
    catch { Start-Sleep -Seconds 2 }
}
if ($saudavel) { Ok "No ar: $url  (painel: $url/admin)" }
else {
    Write-Host "    O /health não respondeu. Veja $PastaSite\logs e o Visualizador de Eventos (Aplicativo, fonte IIS AspNetCore Module V2)." -ForegroundColor Yellow
}
Write-Host "    Depurar: Visual Studio > Depurar > Anexar ao Processo (Ctrl+Alt+P) > w3wp.exe ($IdentidadePool)." -ForegroundColor Gray
if (-not $NaoAbrirNavegador) { Start-Process $url }
