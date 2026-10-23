# 10 — Rodar na sua máquina: Visual Studio, IIS e banco (SSMS)

Há duas formas de rodar o portal localmente. Use a primeira no dia a dia e a segunda para testar como vai ficar no servidor.

| | Visual Studio (F5) | IIS local |
|---|---|---|
| Endereço | https://localhost:7180 | https://localhost:8443 |
| Como sobe | Perfil **https** (Kestrel) ou **IIS Express** | `rodar-iis-local.cmd` ou perfil **IIS local (publicar)** |
| Banco | `(localdb)\MSSQLLocalDB` (appsettings.Development.json) | `localhost` (instância do SQL Server) |
| Ambiente | Development | Local (appsettings.Local.json gerado pelo script) |
| Depurar | F5 direto | Anexar ao processo `w3wp.exe` |

## 1. Visual Studio (F5)

1. Abra `PortalVendas.sln` e deixe **PortalVendas.Web** como projeto de inicialização.
2. No seletor ao lado do botão verde escolha **https** e aperte **F5**.
3. Segredos de teste (Mercado Pago, Melhor Envio): botão direito no projeto Web → **Gerenciar Segredos do Usuário**.

> Visual Studio e `dotnet run` ao mesmo tempo dão erro de arquivo em uso: pare um antes de iniciar o outro.

## 2. IIS local

### Pré-requisitos (uma vez)

1. **IIS**: "Ativar ou desativar recursos do Windows" → Serviços de Informações da Internet (com Ferramentas de Gerenciamento da Web).
2. **ASP.NET Core Hosting Bundle 10**: https://dotnet.microsoft.com/download/dotnet/10.0 → *ASP.NET Core Runtime* → *Hosting Bundle*. Depois rode `iisreset`.
3. **SQL Server** (o IIS não acessa o LocalDB): se o SSMS conecta em `localhost` ou `localhost\SQLEXPRESS`, já serve.
   Se não tiver, a forma mais simples é o **Docker** (o instalador do SQL Server costuma falhar em Windows em português):
   `.\rodar-iis-local.cmd -Docker -CriarBanco` cria o container `portalvendas-sql` (SQL Server 2022 Developer) na porta 14333,
   com senha gerada e guardada cifrada em `C:\PortalVendasLocal\sql-sa.txt`; o site usa um login SQL próprio (`portalvendas_app`).
   No SSMS: servidor `localhost,14333`, *Autenticação do SQL Server*, login `sa` (a senha aparece no fim do script).

### Rodar

Clique duas vezes em **`rodar-iis-local.cmd`** (na raiz do projeto) — ele pede permissão de administrador. Na primeira vez:

```powershell
.\rodar-iis-local.cmd -CriarBanco                          # cria o banco PortalVendas em localhost com dados de exemplo
.\rodar-iis-local.cmd -CriarAdmin vieiraprado@hotmail.com  # cria o administrador nesse banco (pede a senha)
```

Depois, a cada alteração no código, rode de novo sem parâmetros (republica em ~30 s). Outros parâmetros:

| Parâmetro | Para quê |
|---|---|
| `-Servidor "localhost\SQLEXPRESS"` | outra instância do SQL Server |
| `-Docker` | usar o SQL Server no container Docker (use sempre, depois da primeira vez) |
| `-Configuracao Release` | publicar otimizado (padrão Debug, para depurar) |
| `-PortaHttps 9443 -PortaHttp 9080` | se 8443/8080 estiverem ocupadas |
| `-Remover` | apaga o site e o pool do IIS (mantém banco e dados) |

**Pelo Visual Studio:** com o VS aberto **como administrador**, escolha o perfil **IIS local (publicar)** e aperte **Ctrl+F5**.
Se o seu VS não aceitar o perfil, use *Ferramentas → Ferramentas Externas → Adicionar*: Comando `cmd.exe`,
Argumentos `/c "$(SolutionDir)rodar-iis-local.cmd"`, Diretório inicial `$(SolutionDir)`.

**Depurar o site do IIS:** *Depurar → Anexar ao Processo* (Ctrl+Alt+P) → marque "Mostrar processos de todos os usuários" →
`w3wp.exe` do usuário `IIS AppPool\PortalVendasLocal` → Anexar. Os breakpoints passam a funcionar (publicação Debug).

### O que o script monta

| Item | Onde |
|---|---|
| Site e Application Pool | `PortalVendasLocal` no Gerenciador do IIS (pool "Sem código gerenciado") |
| Arquivos publicados | `C:\inetpub\PortalVendasLocal` (logs em `logs\`) |
| Dados | `C:\PortalVendasLocal`: `imagens\`, `chaves\`, `emails\` (e-mails gerados em .html), `licenca.lic` |
| Configuração | `C:\inetpub\PortalVendasLocal\appsettings.Local.json` (banco, URL, pastas + seus user-secrets) |
| HTTPS | certificado "PortalVendas Local" para localhost, confiável nesta máquina |
| Banco | login `IIS AppPool\PortalVendasLocal` com leitura e gravação no banco |

Na primeira vez o script copia do projeto as fotos dos produtos, a `licenca.lic` e as chaves de criptografia
(assim o autenticador do admin e os tokens do WhatsApp continuam funcionando com o banco copiado).

**Se não abrir:** veja `C:\inetpub\PortalVendasLocal\logs` e o *Visualizador de Eventos → Logs do Windows → Aplicativo*
(fonte "IIS AspNetCore Module V2"). Erro 500.19 = falta o Hosting Bundle; 500.30 = erro na inicialização (normalmente banco).

## 3. E-mails de verdade no ambiente local

Sem SMTP, os e-mails (confirmação de cadastro, senha, status do pedido) viram arquivos `.html` em `emails-dev\` (Visual Studio)
ou `C:\PortalVendasLocal\emails\` (IIS). Para enviar de verdade:

```powershell
.\deploy\configurar-email.ps1 -Email suaconta@gmail.com      # pede a senha de app e manda um e-mail de teste
```

- **Gmail** (recomendado): crie uma *senha de app* em https://myaccount.google.com/apppasswords (precisa da verificação em duas etapas).
- **Hotmail/Outlook.com** costumam recusar SMTP com senha; outros provedores: `-Provedor Outro -ServidorSmtp ... -Usuario ...`.
- A conta fica nos *user-secrets* do projeto (fora do repositório). O Visual Studio usa na hora; para o IIS local rode
  `rodar-iis-local.cmd` de novo. Para voltar aos arquivos: `.\deploy\configurar-email.ps1 -Desligar`.

## 4. Banco no SSMS

| Para ver o banco de... | Nome do servidor no SSMS | Autenticação |
|---|---|---|
| Visual Studio / dotnet run | `(localdb)\MSSQLLocalDB` | Windows |
| IIS local | `localhost` (ou `localhost\SQLEXPRESS`) | Windows |

No SSMS 20+: em *Criptografar* escolha **Opcional** (ou marque *Certificado do servidor confiável*). O banco se chama **PortalVendas**.
Consultas prontas (pedidos, canal Site/WhatsApp, produtos, usuários, conversas, configurações): **`db\consultas-uteis.sql`**.

> Não altere tabelas pelo SSMS: mudanças de estrutura vão em `db\migrations` (V003…) e são aplicadas com `db\deploy-db.ps1`.

### Levar os dados do LocalDB para o SQL Server do IIS

Para o IIS usar os mesmos produtos, pedidos e o seu administrador:

```sql
-- 1) No SSMS conectado em (localdb)\MSSQLLocalDB
BACKUP DATABASE PortalVendas TO DISK = N'C:\Users\Public\PortalVendas.bak' WITH INIT, COPY_ONLY;

-- 2) No SSMS conectado em localhost (a instância do IIS)
DECLARE @dados nvarchar(400) = CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400));
DECLARE @log   nvarchar(400) = CAST(SERVERPROPERTY('InstanceDefaultLogPath')  AS nvarchar(400));
DECLARE @sql nvarchar(max) = N'RESTORE DATABASE PortalVendas FROM DISK = N''C:\Users\Public\PortalVendas.bak'' WITH REPLACE, '
    + N'MOVE N''PortalVendas'' TO N''' + @dados + N'PortalVendas.mdf'', '
    + N'MOVE N''PortalVendas_log'' TO N''' + @log + N'PortalVendas_log.ldf''';
EXEC (@sql);
```

Depois rode `rodar-iis-local.cmd` (ele recria o acesso do IIS ao banco restaurado). O restore só funciona se o SQL Server
for da mesma versão do LocalDB ou mais nova.
