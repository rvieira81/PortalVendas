# Portal de Vendas

## Fase atual: 5 — Testes (automatizados OK; falta o roteiro manual)

20/10/2026: build OK, 97/97 testes passando, banco criado no LocalDB com dados de exemplo. Falta rodar o site e executar o roteiro manual com credenciais de teste.

## Escopo do MVP
Loja virtual de um vendedor: vitrine, carrinho, cadastro com CPF, endereço por CEP, frete em tempo real por peso e medidas (Melhor Envio), pagamento por Pix, cartão e boleto (Mercado Pago) e painel administrativo com 2FA.

## Stack decidida
- C# / .NET 10 LTS, ASP.NET Core MVC (Razor), Bootstrap 5.3
- SQL Server 2019+ com scripts versionados (`db/`)
- EF Core 10 (só mapeamento), Serilog, xUnit + SQLite
- Mercado Pago (API v1/payments + Card Payment Brick), Melhor Envio, ViaCEP
- IIS 10 em servidor Windows próprio

## Decisões tomadas (ADRs em docs/adr)
- 001 Monólito modular em 4 camadas
- 002 .NET 10 LTS (o 8 sai de suporte em nov/2026)
- 003 Esquema por scripts SQL versionados, não migrations
- 004 Autenticação por cookie com tabela própria + TOTP no admin
- 005 Mercado Pago com tokenização no navegador (PCI SAQ A)
- 006 Frete pelo Melhor Envio com contingência por faixa de CEP
- 007 Job de expiração/conciliação dentro do processo do IIS (AlwaysRunning)

## Entregáveis
| Fase | Entregável | Situação |
|---|---|---|
| 1 Requisitos | docs/01-requisitos.md | Aprovado |
| 2 Arquitetura | docs/02-arquitetura.md + 7 ADRs | Pronto |
| 3 Banco | docs/03-banco.md, db/migrations/V001, db/seed/S001, db/deploy-db.ps1 | Pronto |
| 4 Desenvolvimento | src/ (4 projetos), todos os RF "Must" e "Should" | Compilado sem erros |
| 5 Testes | tests/ (97 casos), docs/05-testes.md | 97/97 passando; roteiro manual pendente |
| 6 Implantação | deploy/*.ps1, web.config, CI, docs/06-implantacao.md | Pronto |
| 7 Entrega | README.md, docs/07-manual.md | Pronto |
| 9 Módulo WhatsApp | Robô de vendas (Meta Cloud API ou Evolution), IA opcional, Pix na conversa, painel de conversas, pedidos com origem, V002, docs/09-whatsapp.md | Implementado — aguardando compilação local |
| 8 Produto revendável | Licença assinada (tools/PortalVendas.Licenciador), Identidade da loja no painel, vitrine v2 (banner, seções, mini carrinho, frete na página do produto), docs/08-licenciamento.md | Implementado — aguardando compilação local |

## Pendências / riscos abertos
- Ambiente de desenvolvimento usa LocalDB sem criptografia (`Encrypt=False` só em appsettings.Development.json e no deploy-db.ps1 para `(localdb)`).
- Pacotes alinhados em 10.0.13; `System.Security.Cryptography.Pkcs` fixado em 10.0.13 (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7).
- **Credenciais de teste** do Mercado Pago e token do sandbox do Melhor Envio para o roteiro manual (docs/05-testes.md, seção 4).
- **Textos legais** (termos e privacidade) são modelos: revisão jurídica antes do go-live.
- **Nota fiscal** fora do MVP: vendas exigem emissão de NF-e por fora até a integração (backlog).
- Domínio, certificado HTTPS e conta SMTP de produção a definir.
- **Chave do licenciamento:** gerar o par de chaves e colar a pública em `ChavePublicaLicenca.cs` antes de vender (sem ela nenhuma licença é aceita).
- **Proteção da licença** só vale entregando o site compilado (idealmente ofuscado) ou como SaaS — nunca o código-fonte.

## Backlog pós-MVP
- Desconto no Pix (muda o total do pedido e o valor cobrado no Mercado Pago) e cupons de desconto.
- Favoritos, avaliações de produto e newsletter (aparecem na prévia de design, ainda sem backend).
1. Emissão de NF-e (integração com emissor: eNotas, Focus NFe, NFE.io)
2. Geração de etiqueta do Melhor Envio pelo painel (RF22) e rastreio automático
3. Cupons de desconto (RF21) — o pedido já tem a coluna `Desconto`
4. Variações de produto (tamanho/cor) com estoque por variação
5. Avaliações de produtos
6. Login social (Google/Apple)
7. QR Code na tela de configuração do 2FA e proteção contra reuso do mesmo código TOTP
8. Full-Text Search do SQL Server quando o catálogo passar de ~100 mil itens
9. Cache de saída na vitrine e CDN para imagens
10. Script de retenção LGPD para anonimizar endereços de pedidos com mais de 5 anos

## Próximo passo
Criar o administrador (`dotnet run -- criar-admin`), subir o site e executar o roteiro manual de docs/05-testes.md com as credenciais de teste do Mercado Pago e do Melhor Envio.
