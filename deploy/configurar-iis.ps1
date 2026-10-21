<#
.SYNOPSIS
    Prepara o IIS para o PortalVendas (executar UMA vez no servidor, como Administrador).

.DESCRIPTION
    - Cria o Application Pool "PortalVendas" sem .NET CLR (o ASP.NET Core roda pelo AspNetCoreModuleV2)
      com AlwaysRunning e sem idle timeout, para o job de manutenção não parar (ADR-007).
    - Cria o site com binding HTTPS (certificado já instalado em LocalMachine\My).
    - Cria as pastas de dados FORA da pasta do site e dá permissão à identidade do pool.

.PRÉ-REQUISITOS
    - IIS 10 com os recursos: Web Server, Application Initialization, Request Filtering.
    - ASP.NET Core Hosting Bundle 10 (https://dotnet.microsoft.com/download/dotnet/10.0) — reinicie o IIS depois de instalar.

.EXAMPLE
    .\configurar-iis.ps1 -HostName "www.sualoja.com.br" -CertificadoThumbprint "A1B2C3..." -PastaSite "D:\Sites\PortalVendas" -PastaDados "D:\PortalVendas"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $HostName,
    [Parameter(Mandatory)] [string] $CertificadoThumbprint,
    [string] $PastaSite = 'D:\Sites\PortalVendas',
    [string] $PastaDados = 'D:\PortalVendas',
    [string] $NomePool = 'PortalVendas',
    [string] $NomeSite = 'PortalVendas'
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

# ------------------------------------------------------------------ pastas
foreach ($p in @($PastaSite, "$PastaDados\imagens", "$PastaDados\chaves", "$PastaDados\logs")) {
    if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p | Out-Null }
}

# ------------------------------------------------------------------ Application Pool
if (-not (Test-Path "IIS:\AppPools\$NomePool")) { New-WebAppPool -Name $NomePool | Out-Null }
$pool = "IIS:\AppPools\$NomePool"
Set-ItemProperty $pool -Name managedRuntimeVersion -Value ''                  # No Managed Code
Set-ItemProperty $pool -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty $pool -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty $pool -Name processModel.loadUserProfile -Value $true        # necessário para DPAPI
Set-ItemProperty $pool -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Clear-ItemProperty $pool -Name recycling.periodicRestart.schedule
Set-ItemProperty $pool -Name recycling.periodicRestart.schedule -Value @{ value = '03:30:00' }   # reciclagem diária de madrugada
# ASPNETCORE_ENVIRONMENT não definido = Production (padrão do ASP.NET Core)

# ------------------------------------------------------------------ site
if (-not (Test-Path "IIS:\Sites\$NomeSite")) {
    New-Website -Name $NomeSite -PhysicalPath $PastaSite -ApplicationPool $NomePool -HostHeader $HostName -Port 443 -Ssl -SslFlags 1 | Out-Null   # 1 = SNI
    New-WebBinding -Name $NomeSite -Protocol http -Port 80 -HostHeader $HostName   # o site redireciona para HTTPS
}
Set-ItemProperty "IIS:\Sites\$NomeSite" -Name applicationDefaults.preloadEnabled -Value $true

$binding = Get-WebBinding -Name $NomeSite -Protocol https
$binding.AddSslCertificate($CertificadoThumbprint, 'My')

# ------------------------------------------------------------------ permissões (só onde o site precisa escrever)
$identidade = "IIS AppPool\$NomePool"
icacls $PastaSite /grant "${identidade}:(OI)(CI)RX" | Out-Null
foreach ($p in @("$PastaDados\imagens", "$PastaDados\chaves", "$PastaDados\logs")) {
    icacls $p /grant "${identidade}:(OI)(CI)M" | Out-Null
}

Write-Host "IIS configurado. Próximos passos:" -ForegroundColor Green
Write-Host "  1. Copie appsettings.Production.json para $PastaSite (ou configure variáveis de ambiente no pool)."
Write-Host "  2. Rode deploy\publicar.ps1."
Write-Host "  3. Crie o login SQL da identidade '$identidade' (docs/03-banco.md, seção 7)."
