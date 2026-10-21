# 06 — Implantação (IIS + SQL Server próprios)

## 1. Ambientes

| Ambiente | Onde | Banco | Mercado Pago | Melhor Envio | E-mail |
|---|---|---|---|---|---|
| Desenvolvimento | Máquina do dev (`dotnet run` / Visual Studio) | SQL Server local (`PortalVendas`) | Credenciais `TEST-` | `sandbox.melhorenvio.com.br` | Arquivos em `emails-dev\` |
| Homologação | Servidor IIS, site `homolog.sualoja.com.br` | `PortalVendas_Homolog` | Credenciais `TEST-` | Sandbox | SMTP real, só para e-mails da equipe |
| Produção | Servidor IIS, `www.sualoja.com.br` | `PortalVendas` | Credenciais `APP_USR-` | `melhorenvio.com.br` | SMTP real |

A configuração vem de `appsettings.json` + `appsettings.{Ambiente}.json` + **variáveis de ambiente** (que têm prioridade). O ambiente é definido por `ASPNETCORE_ENVIRONMENT` (não definido = `Production`).

## 2. Segredos (RNF04)

Nunca no repositório. Escolha **uma** das formas no servidor:

**a) Arquivo só no servidor** — `appsettings.Production.json` na pasta do site, com permissão de leitura apenas para Administradores e `IIS AppPool\PortalVendas`. O `.gitignore` já o exclui e o `publicar.ps1` nunca o sobrescreve. Modelo: `src/PortalVendas.Web/appsettings.Production.exemplo.json`.

**b) Variáveis de ambiente do Application Pool** (IIS 10):

```powershell
$pool = "system.applicationHost/applicationPools/add[@name='PortalVendas']/environmentVariables"
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter $pool -name '.' -value @{ name='MercadoPago__AccessToken'; value='APP_USR-...' }
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter $pool -name '.' -value @{ name='MercadoPago__SegredoWebhook'; value='...' }
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter $pool -name '.' -value @{ name='MelhorEnvio__Token'; value='...' }
Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter $pool -name '.' -value @{ name='Smtp__Senha'; value='...' }
```

Em desenvolvimento, use `dotnet user-secrets` (o projeto Web já tem `UserSecretsId`):

```powershell
cd src\PortalVendas.Web
dotnet user-secrets set "MercadoPago:AccessToken" "TEST-..."
dotnet user-secrets set "MercadoPago:PublicKey" "TEST-..."
dotnet user-secrets set "MercadoPago:SegredoWebhook" "..."
dotnet user-secrets set "MelhorEnvio:Token" "..."
```

## 3. Preparação do servidor (uma vez)

1. **IIS 10** com: Web Server, **Application Initialization**, Request Filtering, URL Rewrite (opcional).
2. **ASP.NET Core Hosting Bundle 10** — https://dotnet.microsoft.com/download/dotnet/10.0 → depois `iisreset`.
3. **Certificado HTTPS** instalado em `Certificados (Computador local)\Pessoal` (anote a impressão digital).
4. Rodar como Administrador:
   ```powershell
   .\deploy\configurar-iis.ps1 -HostName "www.sualoja.com.br" -CertificadoThumbprint "A1B2..." `
       -PastaSite "D:\Sites\PortalVendas" -PastaDados "D:\PortalVendas"
   ```
   Cria o Application Pool **sem código gerenciado**, com `AlwaysRunning`, `idleTimeout = 0`, `preloadEnabled` e `loadUserProfile` (necessário para a DPAPI), o site com HTTPS (SNI) e as pastas `imagens`, `chaves` e `logs` fora do site, com permissão de escrita só para o pool.
5. **Banco** (como DBA):
   ```powershell
   .\db\deploy-db.ps1 -Servidor "SRVSQL01" -Banco "PortalVendas" -CriarBanco
   ```
   E o login da aplicação com `db_datareader` + `db_datawriter` (ver `docs/03-banco.md`, seção 7). A string de conexão de produção usa `Trusted_Connection=True` com a identidade do pool (se o SQL estiver em outro servidor, use uma conta de domínio no pool ou um login SQL).
6. **Backups**: agendar os blocos de `deploy/backup-banco.sql` no SQL Server Agent (completo 02:00, diferencial a cada 6 h, log a cada 15 min).
7. **Administrador** (na pasta do site, depois da primeira publicação):
   ```powershell
   cd D:\Sites\PortalVendas
   $env:ASPNETCORE_ENVIRONMENT = "Production"
   dotnet .\PortalVendas.Web.dll criar-admin admin@sualoja.com.br "Nome do Administrador"
   ```
   A senha é pedida sem eco (mínimo 12 caracteres). No primeiro login em `/conta/entrar` o sistema pede para configurar o autenticador.

## 4. Mercado Pago

1. **Suas integrações → Criar aplicação** (Checkout Transparente / Pagamentos online).
2. **Credenciais de teste** para homologação; **credenciais de produção** depois da homologação aprovada pelo Mercado Pago.
3. **Webhooks → Configurar notificações**: URL `https://www.sualoja.com.br/webhooks/mercadopago`, evento **Pagamentos**. Copie a **assinatura secreta** para `MercadoPago:SegredoWebhook`.
4. Preencha `MercadoPago:UrlNotificacao` com a mesma URL.
5. Use o botão **Simular** do painel de webhooks para conferir: o log deve registrar a notificação e a tabela `NotificacaoGateway` ganha uma linha com `AssinaturaValida = 1`.

## 5. Melhor Envio

1. Crie a conta em melhorenvio.com.br (e no sandbox para homologação).
2. **Integrações → Permissões de acesso → Gerar token** com o escopo `shipping-calculate`.
3. Configure `MelhorEnvio:Token`, `MelhorEnvio:EmailContato` e `MelhorEnvio:UrlBase` (`https://melhorenvio.com.br/` em produção).
4. Opcional: `MelhorEnvio:Servicos` para limitar as transportadoras (ex.: `"1,2,3,4,17"` = PAC, SEDEX, Jadlog Package, Jadlog .Com, Mini Envios).
5. No painel admin → **Frete e pagamento**, informe o **CEP de origem** e os **dias de manuseio**.

## 6. Publicação de uma nova versão

```powershell
# 1) Backup do banco ANTES de qualquer script novo
sqlcmd -S SRVSQL01 -Q "BACKUP DATABASE PortalVendas TO DISK='E:\Backups\SQL\PortalVendas\pre-deploy.bak' WITH COPY_ONLY, COMPRESSION, CHECKSUM"

# 2) Scripts de banco pendentes (mostra antes de aplicar)
.\db\deploy-db.ps1 -Servidor "SRVSQL01" -Banco "PortalVendas" -SomenteListar
.\db\deploy-db.ps1 -Servidor "SRVSQL01" -Banco "PortalVendas"

# 3) Site: testes, publish, backup da versão atual, app_offline, cópia, /health; reverte sozinho se o /health falhar
.\deploy\publicar.ps1 -PastaSite "D:\Sites\PortalVendas" -UrlSite "https://www.sualoja.com.br"
```

O `publicar.ps1` pode rodar da máquina de build com a pasta do site compartilhada, ou no próprio servidor se ele tiver o SDK. Alternativa: usar o artefato `portalvendas-web` gerado pelo GitHub Actions (`.github/workflows/ci.yml`).

## 7. Rollback

| O quê | Como |
|---|---|
| **Site** | `.\deploy\publicar.ps1 -PastaSite ... -UrlSite ... -RestaurarBackup "D:\Backups\PortalVendas\site-AAAAMMDD-HHMM.zip"` |
| **Banco — script com rollback** | Rodar `db/rollback/R00N__....sql` correspondente |
| **Banco — sem rollback** | Restaurar o `pre-deploy.bak` (perde o que entrou depois; avaliar com o negócio) |

**Ensaio obrigatório em homologação** antes do primeiro go-live: publicar, restaurar o backup do site e conferir que a versão anterior volta respondendo `/health`.

## 8. Checklist de go-live

- [ ] HTTPS com certificado válido; HTTP redireciona para HTTPS; HSTS ativo (só em produção)
- [ ] `AllowedHosts` com o domínio real
- [ ] `Loja:UrlBase` com o domínio real (links dos e-mails)
- [ ] Segredos configurados fora do repositório (Mercado Pago produção, Melhor Envio produção, SMTP)
- [ ] Webhook do Mercado Pago apontando para produção e testado com "Simular"
- [ ] `DataProtection:PastaChaves` e `Imagens:Pasta` apontando para `D:\PortalVendas\...` (fora do site)
- [ ] Application Pool com `AlwaysRunning` e site com `preloadEnabled` (o job roda a cada 5 minutos: conferir a linha "Manutenção" no log)
- [ ] Logs gravando em `D:\PortalVendas\logs`
- [ ] Backups agendados e **um restore testado**
- [ ] Administrador criado e com autenticador configurado; segundo administrador de contingência
- [ ] **Dados de exemplo removidos** (o seed não roda em produção; se rodou em homologação, não copie aquele banco)
- [ ] CEP de origem, dias de manuseio, frete grátis e parcelas configurados no painel
- [ ] Textos de **Termos de uso** e **Política de privacidade** revisados pelo jurídico
- [ ] Monitor externo (UptimeRobot, Zabbix, PRTG) chamando `https://www.sualoja.com.br/health` a cada 1 minuto
- [ ] Roteiro manual de `docs/05-testes.md` executado em homologação sem bugs críticos/altos

## 9. Monitoramento e operação

| O quê | Onde |
|---|---|
| Saúde | `GET /health` → 200 (banco acessível) ou 503 |
| Logs | `D:\PortalVendas\logs\portal-AAAAMMDD.log` (30 dias). Cada erro mostra o `RequestId`, o mesmo que aparece para o cliente na página de erro |
| Webhooks | `SELECT TOP 50 * FROM NotificacaoGateway ORDER BY Id DESC` — `AssinaturaValida = 0` ou `Erro` preenchido merecem atenção |
| Pagamentos parados | `SELECT * FROM Pagamento WHERE Status IN (1,2) AND CriadoEm < DATEADD(HOUR,-2,SYSUTCDATETIME())` |
| Alertas sugeridos | Linhas `[ERR]`/`[FTL]` no log; "assinatura inválida" repetida; "usando a tabela de contingência" (Melhor Envio fora); `/health` fora |

### Administrador perdeu o celular do autenticador

Outro administrador (ou o DBA) zera o segundo fator; no próximo login o sistema pede para configurar de novo:

```sql
UPDATE dbo.Usuario
   SET TotpAtivo = 0, TotpSegredoProtegido = NULL, CarimboSeguranca = NEWID()
 WHERE Email = 'admin@sualoja.com.br' AND Perfil = 2;
```

### Chaves do Data Protection

Ficam em `DataProtection:PastaChaves`, cifradas com DPAPI da máquina. **Faça backup dessa pasta.** Se ela se perder, os clientes precisam entrar de novo e os administradores precisam reconfigurar o autenticador (o segredo TOTP é cifrado com essas chaves). Em um web farm, a pasta precisa ser compartilhada e a proteção trocada para certificado.
