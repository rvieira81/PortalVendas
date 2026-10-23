# Portal de Vendas

Loja virtual de um vendedor em **C# / ASP.NET Core MVC (.NET 10)** com **SQL Server**: vitrine com busca e filtros, carrinho, cadastro com CPF, endereço com preenchimento pelo CEP, **frete em tempo real por peso e medidas (Melhor Envio)**, pagamento por **Pix, cartão em até 12x e boleto (Mercado Pago)** e painel administrativo com segundo fator.

| Documento | Conteúdo |
|---|---|
| [PROJETO.md](PROJETO.md) | Estado do projeto, decisões e próximos passos |
| [docs/01-requisitos.md](docs/01-requisitos.md) | Requisitos, critérios de aceite e regras de negócio |
| [docs/02-arquitetura.md](docs/02-arquitetura.md) | Arquitetura, stack, segurança e [ADRs](docs/adr) |
| [docs/03-banco.md](docs/03-banco.md) | Modelo de dados, índices, LGPD e backup |
| [docs/05-testes.md](docs/05-testes.md) | Plano de testes, rastreabilidade e roteiros manuais |
| [docs/06-implantacao.md](docs/06-implantacao.md) | IIS, segredos, publicação, rollback e go-live |
| [docs/07-manual.md](docs/07-manual.md) | Manual do cliente e do administrador |
| [docs/08-licenciamento.md](docs/08-licenciamento.md) | Licença por loja (nome, CNPJ, domínio) e identidade visual |

## Rodar localmente

**Pré-requisitos:** .NET SDK 10, SQL Server (LocalDB, Express ou Developer), PowerShell.

```powershell
# 1. Banco: cria, aplica o esquema e carrega dados de exemplo
.\db\deploy-db.ps1 -Servidor "localhost" -Banco "PortalVendas" -CriarBanco -ComSeed
#    (LocalDB: -Servidor "(localdb)\MSSQLLocalDB" e ajuste a ConnectionString em appsettings.json)

# 2. Credenciais de TESTE (ficam fora do repositório)
cd src\PortalVendas.Web
dotnet user-secrets set "MercadoPago:AccessToken" "TEST-..."
dotnet user-secrets set "MercadoPago:PublicKey" "TEST-..."
dotnet user-secrets set "MercadoPago:SegredoWebhook" "..."
dotnet user-secrets set "MelhorEnvio:Token" "..."        # sandbox; sem token, usa a tabela de contingência

# 3. Administrador
dotnet run -- criar-admin admin@teste.com.br "Administrador"

# 4. Subir o site
dotnet run --launch-profile https
# https://localhost:7180  — painel em https://localhost:7180/admin
```

Em desenvolvimento os e-mails (confirmação de cadastro, pedidos) são gravados como `.html` em `src\PortalVendas.Web\emails-dev\` — abra o arquivo para clicar no link de confirmação.

Para testar o **webhook** localmente, exponha a porta com um túnel (ngrok, Dev Tunnels do Visual Studio) e configure a URL pública no painel do Mercado Pago e em `MercadoPago:UrlNotificacao`. Sem webhook, o job de conciliação confirma os pagamentos em até 10 minutos.

## Licença e identidade da loja

Nome, razão social, CNPJ e domínios vêm do arquivo `licenca.lic`, assinado pelo fornecedor; logo, cores, contatos e banner
o lojista altera em **Painel → Identidade da loja**. Sem licença o site roda como demonstração, só em `localhost`.
Gerar chaves e emitir licenças: [docs/08-licenciamento.md](docs/08-licenciamento.md).

## Testes

```powershell
dotnet test PortalVendas.sln
```

97 casos: regras de domínio, serviços com banco SQLite em memória (estoque atômico, idempotência, webhook, expiração, estorno, IDOR, bloqueio de login, MFA) e leitura das respostas do Mercado Pago e do Melhor Envio.

## Docker

```bash
docker build -t portalvendas-web .
docker run -p 8080:8080 \
  -e ConnectionStrings__PortalVendas="Server=...;Database=PortalVendas;..." \
  portalvendas-web
```

A imagem roda como usuário sem privilégios e escuta na porta 8080. Segredos e `appsettings.Production.json` não entram na imagem (ver `.dockerignore`); passe-os por variáveis de ambiente.

## Estrutura

```
src/PortalVendas.Domain          entidades e regras (sem dependências)
src/PortalVendas.Application     casos de uso, interfaces das integrações
src/PortalVendas.Infrastructure  EF Core, Mercado Pago, Melhor Envio, ViaCEP, SMTP, job
src/PortalVendas.Web             MVC, área Admin, autenticação, segurança
tests/PortalVendas.Tests         xUnit
db/                              scripts SQL versionados + deploy-db.ps1
deploy/                          configurar-iis.ps1, publicar.ps1, backup-banco.sql
```

## Usar .NET 8 em vez de 10

Troque `net10.0` por `net8.0` em `Directory.Build.props` e as versões `10.0.0` por `8.0.11` em `Directory.Packages.props`. O código não usa recursos exclusivos do .NET 10. Atenção: o suporte do .NET 8 termina em 10/11/2026.
