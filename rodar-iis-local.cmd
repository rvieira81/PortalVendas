@echo off
rem Publica e abre o PortalVendas no IIS local (https://localhost:8443).
rem Duplo clique ou: rodar-iis-local.cmd -CriarBanco   (parametros em deploy\iis-local.ps1)
net session >nul 2>&1
if %errorlevel% neq 0 (
  if "%~1"=="" (
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
  ) else (
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0' -ArgumentList '%*'"
  )
  exit /b
)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -NoExit -File "%~dp0deploy\iis-local.ps1" %*
