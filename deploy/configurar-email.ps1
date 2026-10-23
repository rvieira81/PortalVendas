<#
.SYNOPSIS
    Liga o envio de e-mails de verdade no ambiente local (Visual Studio e IIS local).

.DESCRIPTION
    Sem SMTP configurado, o portal grava os e-mails como arquivos .html (pasta emails-dev ou C:\PortalVendasLocal\emails).
    Este script guarda a conta de envio nos user-secrets do projeto (fora do repositório), manda um e-mail de teste
    e pronto: o Visual Studio (F5) já envia; para o IIS local, rode de novo o rodar-iis-local.cmd (ele copia os user-secrets).

    Gmail: use uma "senha de app" (https://myaccount.google.com/apppasswords — exige verificação em duas etapas),
    não a senha normal da conta.

.EXAMPLE
    .\deploy\configurar-email.ps1 -Email minhaconta@gmail.com
.EXAMPLE
    .\deploy\configurar-email.ps1 -Provedor Outro -ServidorSmtp smtp-relay.brevo.com -Usuario 123abc@smtp-brevo.com -Email loja@meudominio.com.br
.EXAMPLE
    .\deploy\configurar-email.ps1 -Desligar     # volta a gravar os e-mails em arquivo
#>
[CmdletBinding()]
param(
    [ValidateSet('Gmail', 'Outlook', 'Outro')] [string] $Provedor = 'Gmail',
    [string] $Email,
    [string] $Usuario,
    [string] $ServidorSmtp,
    [int] $Porta = 587,
    [switch] $SemTeste,
    [switch] $Desligar
)
$ErrorActionPreference = 'Stop'
$projeto = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\PortalVendas.Web'

function Segredo([string] $chave, [string] $valor) {
    & dotnet user-secrets set $chave $valor --project $projeto | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Não foi possível gravar $chave nos user-secrets." }
}

if ($Desligar) {
    foreach ($c in 'Smtp:Host', 'Smtp:Usuario', 'Smtp:Senha') { & dotnet user-secrets remove $c --project $projeto | Out-Null }
    Write-Host 'Envio desligado: os e-mails voltam a ser gravados em arquivo.' -ForegroundColor Yellow
    exit 0
}

if (-not $Email) { $Email = Read-Host 'E-mail que vai enviar (remetente)' }
if (-not $Usuario) { $Usuario = $Email }
switch ($Provedor) {
    'Gmail' { if (-not $ServidorSmtp) { $ServidorSmtp = 'smtp.gmail.com' } }
    'Outlook' { if (-not $ServidorSmtp) { $ServidorSmtp = 'smtp-mail.outlook.com' } }
    'Outro' { if (-not $ServidorSmtp) { $ServidorSmtp = Read-Host 'Servidor SMTP' } }
}
if ($Provedor -eq 'Gmail') {
    Write-Host 'Gmail: use a SENHA DE APP de 16 letras (https://myaccount.google.com/apppasswords), não a senha da conta.' -ForegroundColor Gray
}
$senhaSegura = Read-Host "Senha SMTP de $Usuario" -AsSecureString
$senha = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($senhaSegura))
if ($Provedor -eq 'Gmail') { $senha = $senha -replace '\s', '' }   # a senha de app aparece com espaços

if (-not $SemTeste) {
    Write-Host "Enviando e-mail de teste para $Email por $ServidorSmtp`:$Porta..." -ForegroundColor Cyan
    try {
        $cred = New-Object System.Management.Automation.PSCredential($Usuario, (ConvertTo-SecureString $senha -AsPlainText -Force))
        Send-MailMessage -SmtpServer $ServidorSmtp -Port $Porta -UseSsl -Credential $cred -From $Email -To $Email `
            -Subject 'PortalVendas: teste de envio' -Body 'O envio de e-mails do ambiente local está funcionando.' -Encoding UTF8 `
            -WarningAction SilentlyContinue
        Write-Host 'Teste enviado. Confira a caixa de entrada (e o spam).' -ForegroundColor Green
    } catch {
        Write-Host "O servidor recusou: $($_.Exception.Message)" -ForegroundColor Red
        if ($Provedor -eq 'Gmail') { Write-Host 'Confira se é a senha de app e se a verificação em duas etapas está ligada.' -ForegroundColor Yellow }
        if ($Provedor -eq 'Outlook') { Write-Host 'Contas Hotmail/Outlook.com podem não aceitar SMTP com senha. Prefira Gmail com senha de app.' -ForegroundColor Yellow }
        Write-Host 'Nada foi gravado. Corrija e rode de novo (ou use -SemTeste para gravar mesmo assim).' -ForegroundColor Yellow
        exit 1
    }
}

Segredo 'Smtp:Host' $ServidorSmtp
Segredo 'Smtp:Porta' "$Porta"
Segredo 'Smtp:UsarSsl' 'true'
Segredo 'Smtp:Usuario' $Usuario
Segredo 'Smtp:Senha' $senha
Segredo 'Smtp:Remetente' $Email

Write-Host "`nPronto: conta de envio guardada nos user-secrets do projeto (fora do repositório)." -ForegroundColor Green
Write-Host '  Visual Studio: pare e inicie de novo (F5) — os e-mails passam a ser enviados.' -ForegroundColor Green
Write-Host '  IIS local: rode .\rodar-iis-local.cmd -Docker (com as portas que você usa) para copiar a configuração.' -ForegroundColor Green
