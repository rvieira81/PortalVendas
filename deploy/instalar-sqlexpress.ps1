<#
.SYNOPSIS
    Instala o SQL Server 2022 Express (instância SQLEXPRESS) em Windows em português (execute como Administrador).

.DESCRIPTION
    O instalador do SQL Server em inglês falha em Windows pt-BR ("setup media does not support the language of the OS").
    Este script baixa a mídia, extrai e instala com /ENU, que resolve esse erro:
      1. winget download do instalador (SSEI) para C:\SQLMedia
      2. SSEI /ACTION=Download (mídia Core) e extração do pacote
      3. setup.exe /ENU silencioso: instância SQLEXPRESS, seu usuário como sysadmin, TCP ligado
    Depois: .\rodar-iis-local.cmd -CriarBanco -Servidor "localhost\SQLEXPRESS"
#>
[CmdletBinding()]
param(
    [string] $Pasta = 'C:\SQLMedia',
    [string] $Instancia = 'SQLEXPRESS'
)
$ErrorActionPreference = 'Stop'

$eu = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $eu.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Execute como Administrador.' }
if (Get-Service -Name "MSSQL`$$Instancia" -ErrorAction SilentlyContinue) {
    Write-Host "A instância $Instancia já existe. Use -Servidor ""localhost\$Instancia""." -ForegroundColor Green
    exit 0
}
New-Item -ItemType Directory -Path $Pasta -Force | Out-Null

Write-Host '==> Baixando o instalador (winget)' -ForegroundColor Cyan
& winget download --id Microsoft.SQLServer.2022.Express -e -d $Pasta --accept-package-agreements --accept-source-agreements
$ssei = Get-ChildItem $Pasta -Filter '*.exe' | Where-Object { $_.Name -notlike 'SQLEXPR*' } | Select-Object -First 1
if (-not $ssei) { throw "Instalador não encontrado em $Pasta." }

Write-Host '==> Baixando a mídia do SQL Server (pode levar alguns minutos)' -ForegroundColor Cyan
& $ssei.FullName /ACTION=Download /MEDIAPATH=$Pasta /MEDIATYPE=Core /QUIET | Out-Host
$pacote = Get-ChildItem $Pasta -Filter 'SQLEXPR*.exe' | Select-Object -First 1
if (-not $pacote) { throw "A mídia não foi baixada em $Pasta (veja a mensagem acima)." }

Write-Host '==> Extraindo' -ForegroundColor Cyan
$setupDir = Join-Path $Pasta 'Setup'
Start-Process -FilePath $pacote.FullName -ArgumentList "/q /x:`"$setupDir`"" -Wait

Write-Host "==> Instalando a instância $Instancia (5 a 10 minutos)" -ForegroundColor Cyan
$usuario = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$argumentos = @(
    '/Q', '/ENU', '/ACTION=Install', '/FEATURES=SQLEngine', "/INSTANCENAME=$Instancia",
    "/SQLSYSADMINACCOUNTS=`"$usuario`"", '/TCPENABLED=1', '/IACCEPTSQLSERVERLICENSETERMS', '/UPDATEENABLED=0'
)
$proc = Start-Process -FilePath (Join-Path $setupDir 'setup.exe') -ArgumentList $argumentos -Wait -PassThru
if ($proc.ExitCode -ne 0 -and $proc.ExitCode -ne 3010) {
    throw "A instalação falhou (código $($proc.ExitCode)). Log: C:\Program Files\Microsoft SQL Server\160\Setup Bootstrap\Log\Summary.txt"
}
if ($proc.ExitCode -eq 3010) { Write-Host 'Instalado. O Windows pede reinicialização para concluir.' -ForegroundColor Yellow }

Write-Host "`nSQL Server Express instalado: localhost\$Instancia ($usuario é administrador do SQL)." -ForegroundColor Green
Write-Host "Próximo passo: .\rodar-iis-local.cmd -CriarBanco -Servidor ""localhost\$Instancia""" -ForegroundColor Green
