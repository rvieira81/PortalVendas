# 08 — Licenciamento e identidade da loja

O PortalVendas é vendido para várias lojas. Cada instalação recebe um **arquivo de licença** (`licenca.lic`)
emitido pelo fornecedor (Prado). A licença define o que o comprador **não** pode mudar sozinho:

| Vem da licença (só o fornecedor altera) | Vem do painel (o lojista altera) |
|---|---|
| Nome da loja, razão social, CNPJ | Logo, cor principal e cor de destaque |
| Domínios em que a loja pode funcionar | WhatsApp, telefone, e-mail, endereço, horário |
| Módulos pagos (ex.: vendas pelo WhatsApp) | Configuração do WhatsApp (provedor, robô, IA) |
| Validade (opcional) e direito a atualizações | Redes sociais, faixa de aviso, banner da página inicial |

## Como funciona

- A licença é um JSON assinado com **ECDSA P-256 / SHA-256**. O site guarda só a **chave pública**
  (`src/PortalVendas.Infrastructure/Licenciamento/ChavePublicaLicenca.cs`); a **chave privada** fica só com o fornecedor.
  Qualquer alteração no arquivo (trocar o nome, o CNPJ ou o domínio) invalida a assinatura.
- O site só atende nos **domínios da licença**. Em outro endereço a vitrine mostra "Loja temporariamente indisponível"
  (HTTP 503). Continuam acessíveis: `/health`, arquivos estáticos, login (`/conta`), painel (`/admin`, para instalar a licença)
  e o webhook do Mercado Pago (pagamentos em andamento não se perdem).
- **Sem licença** (ou com licença inválida/vencida) a loja roda como **demonstração**: nome "Loja Demonstração",
  CNPJ 00.000.000/0000-00, faixa amarela no topo e só em `localhost` acessado do próprio servidor. Assim o
  desenvolvimento local continua funcionando sem arquivo nenhum.
- O nome da licença também aparece no remetente dos e-mails e no aplicativo autenticador (TOTP) do administrador.

## Passo a passo do fornecedor

### 1. Gerar o par de chaves (uma vez só)

```powershell
dotnet run --project tools/PortalVendas.Licenciador -- chaves --saida C:\Licencas\chave-privada.pem
```

- Cole a chave pública exibida em `ChavePublicaLicenca.cs` (`public const string Valor = "...";`), recompile e publique.
- Guarde `chave-privada.pem` **fora do repositório** (o `.gitignore` já bloqueia `*.pem` e `*.lic`), com backup em local seguro.
  Perdeu a chave: não dá mais para emitir licenças compatíveis. Vazou: gere outro par, troque a chave pública e reemita as licenças.

### 2. Emitir a licença de um cliente

```powershell
dotnet run --project tools/PortalVendas.Licenciador -- emitir `
  --chave C:\Licencas\chave-privada.pem `
  --nome "Prado Shop" --razao "Prado Shop Comércio Ltda." --cnpj 12.345.678/0001-95 `
  --dominio pradoshop.com.br --dominio www.pradoshop.com.br `
  --atualizacoes-ate 2027-10-31 `
  --saida C:\Licencas\pradoshop.lic
```

- `--expira aaaa-mm-dd` cria licença com vencimento (assinatura mensal/anual). Sem ele, a licença é perpétua.
- `--dominio *.cliente.com.br` libera todos os subdomínios.
- `--modulo whatsapp` libera o módulo de vendas pelo WhatsApp (ver [09-whatsapp.md](09-whatsapp.md)). Sem ele, o módulo fica bloqueado.
- O CNPJ é conferido (aceita também o CNPJ alfanumérico da Receita).
- Conferir um arquivo: `dotnet run --project tools/PortalVendas.Licenciador -- verificar --arquivo C:\Licencas\pradoshop.lic`

### 3. Instalação no cliente

O administrador da loja entra em **Painel → Identidade da loja → Instalar licença** e envia o `.lic`.
Alternativa: copiar o arquivo para a pasta do site (ou o caminho em `Licenca:Arquivo` do appsettings).
A identidade do Application Pool precisa de **permissão de escrita** nesse arquivo/pasta para a instalação pelo painel.

Trocar nome, razão social, CNPJ ou domínio = emitir uma nova licença e o cliente instalar por cima.

## Limites da proteção (importante)

- A proteção só é efetiva se o cliente receber **apenas o site compilado** (`dotnet publish`), nunca o código-fonte.
  Com o código-fonte em mãos, qualquer programador remove a verificação.
- Para dificultar a engenharia reversa do binário, use um ofuscador .NET no `PortalVendas.Infrastructure.dll`
  (ex.: ConfuserEx, Obfuscar, Dotfuscator) — opcional.
- A forma mais forte é o modelo **SaaS**: você hospeda e o cliente só recebe o acesso.
- Não publique o projeto `tools/PortalVendas.Licenciador` junto com o site.
