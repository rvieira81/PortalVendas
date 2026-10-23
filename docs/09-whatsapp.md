# 09 — Vendas e atendimento pelo WhatsApp (módulo pago)

O módulo transforma o WhatsApp da loja em um canal de vendas ligado ao portal: o cliente vê categorias e produtos
(com foto e preço do catálogo), monta o carrinho, informa os dados e o endereço, escolhe o frete e paga por **Pix
na própria conversa** ou por um **link para cartão ou boleto** no site. O pedido entra no painel como qualquer outro,
marcado como **WhatsApp**, e o cliente recebe na conversa os avisos de pagamento, envio (com rastreio) e entrega.

## Como o módulo é liberado

O módulo só funciona com uma licença válida que o inclua (`--modulo whatsapp`). Sem ele o menu do painel mostra a
apresentação do módulo e os webhooks respondem 404 — nem em modo demonstração o robô funciona.

```powershell
dotnet run --project tools/PortalVendas.Licenciador -- emitir --chave C:\Licencas\chave-privada.pem `
  --nome "Prado Shop" --razao "Prado Shop Comércio Ltda." --cnpj 12.345.678/0001-95 `
  --dominio pradoshop.com.br --dominio www.pradoshop.com.br --modulo whatsapp --saida C:\Licencas\pradoshop.lic
```

O cliente instala o novo `.lic` em **Painel → Identidade da loja** (a licença antiga continua valendo até ser substituída).

## Instalação

1. Banco: rode `db\deploy-db.ps1` — aplica `V002__whatsapp.sql` (conversas, mensagens, origem do pedido).
2. `Loja:UrlBase` no appsettings precisa ser o endereço público **https** da loja, dentro do domínio da licença:
   a Meta/Evolution chamam o webhook nele e o WhatsApp baixa as fotos dos produtos por ele.
3. **Painel → WhatsApp → Configuração**: escolha o provedor, preencha os dados, salve e use **Enviar teste**.

### Opção A — API oficial da Meta (recomendada)

| Onde | O que fazer |
|---|---|
| developers.facebook.com | Criar um app do tipo *Business* e adicionar o produto **WhatsApp** |
| WhatsApp → Configuração da API | Cadastrar e verificar o número da loja; copiar o **Phone Number ID** |
| Business Manager → Usuários do sistema | Criar um usuário do sistema, gerar **token permanente** com `whatsapp_business_messaging` e `whatsapp_business_management` |
| App → Configurações → Básico | Copiar o **App Secret** (confere a assinatura de cada mensagem recebida) |
| WhatsApp → Configuração → Webhook | Callback URL e Verify token: copie da tela de configuração do painel; assinar o campo **messages** |
| Gerenciador do WhatsApp → Modelos (opcional) | Modelo de *Utilidade* em pt_BR com 3 variáveis: {{1}} pedido, {{2}} situação, {{3}} rastreio — informe o nome no painel |

- Regra das 24 h: a loja só pode mandar mensagem livre até 24 h depois da última mensagem do cliente. Avisos de
  status fora dessa janela usam o modelo; sem modelo, o aviso fica registrado no painel como "não entregue".
- Custo: a Meta cobra por conversa iniciada pela loja (modelos). Conversas iniciadas pelo cliente têm cota gratuita.
- Até 3 opções curtas viram **botões**; até 10, uma **lista**; o cliente também pode responder com o número.

### Opção B — Evolution API (não oficial)

Usa o número que a loja já tem, conectado lendo um QR Code (como o WhatsApp Web). **Risco:** a Meta pode bloquear
números que usam integrações não oficiais — use para começar rápido ou em lojas pequenas e prefira a API oficial.

1. Suba a Evolution API v2 (Docker, em um servidor da loja) e crie uma **instância**; leia o QR Code com o celular da loja.
2. No painel informe endereço, instância e API key.
3. Na instância, configure o **webhook** com a URL mostrada no painel, evento **MESSAGES_UPSERT** e
   **"Webhook by Events" desligado**. A URL contém um segredo; se vazar, use "Gerar novo endereço".
4. As opções vão numeradas no texto ("1. Ver produtos") e o cliente responde com o número.

### IA (opcional)

Com a chave da API da Anthropic (console.anthropic.com), o robô entende mensagens livres — "vocês têm panela de
pressão?", "qual o prazo pra Campinas?", "quero falar com alguém". A IA **só** classifica a intenção, extrai os
termos de busca e responde dúvidas com as informações da loja (pagamento, frete grátis, horário, endereço);
produtos, preços e estoque sempre vêm do catálogo. Sem IA, o robô usa a busca por palavras do próprio catálogo.
Custo: cobrado por uso na conta da Anthropic — cada mensagem livre faz uma chamada curta ao modelo (padrão: Haiku). Acompanhe o gasto no console da Anthropic.

## O fluxo da conversa

```
oi → menu (Ver produtos · Buscar · Meu carrinho · Meus pedidos · Atendente)
  → categoria → produto (foto, preço, parcelas, estoque) → Adicionar → quantidade
  → Finalizar compra → nome → CPF → e-mail (opcional) → CEP → número → complemento
  → opções de frete (cotação real) → resumo → Pagar com Pix | Cartão ou boleto
  → Pix copia e cola na conversa + link do site · "pagamento confirmado" quando cair
```

- Na primeira compra o robô cria a conta do cliente **sem senha** (só o WhatsApp dá acesso). Nas próximas, o número
  identifica a conta. Contas do site **não** são vinculadas automaticamente pelo telefone (o telefone do site não é
  verificado); se o CPF já existe no site, o robô orienta comprar pelo site ou chama o atendente.
- O link de pagamento entra na conta por 24 h e abre a página de pagamento do pedido (cartão, boleto ou Pix).
- Áudio, foto e figurinha: o robô avisa que entende só texto.
- Depois de 6 h sem mensagem, a conversa recomeça pelo menu (o carrinho é mantido).
- "atendente" (ou o botão) passa a conversa para o painel: o robô fica quieto até alguém **Devolver ao robô**.

## Painel

- **WhatsApp**: lista de conversas (as que esperam atendente aparecem primeiro, com contador no menu),
  histórico no formato de chat, resposta do atendente, assumir/devolver ao robô e os pedidos do cliente.
- **Pedidos**: filtro por canal (Site/WhatsApp) e selo "WhatsApp" na lista e no detalhe.
- **Painel de vendas**: faturamento do período feito pelo WhatsApp.

## Segurança

- Meta: toda mensagem recebida tem a assinatura HMAC conferida com o App Secret; sem App Secret a configuração não salva.
- Evolution: o webhook só aceita a URL com o token secreto (comparação em tempo constante).
- Tokens, API key e chave da IA ficam **cifrados** no banco (Data Protection); a tela nunca mostra o valor salvo.
- Mensagens repetidas pelo provedor são ignoradas (id da mensagem único).
- A IA recebe a mensagem do cliente marcada como dado e instruções para ignorar tentativas de mudar as regras;
  ela não tem acesso a pedidos, clientes nem ao banco.
- As respostas são processadas em segundo plano, uma por vez e na ordem de chegada; se o site reiniciar,
  as mensagens da última hora sem resposta são retomadas.

## Testar na sua máquina

O provedor precisa alcançar o site pela internet. Use um túnel (ex.: `cloudflared tunnel --url https://localhost:7180`
ou ngrok), coloque o endereço do túnel em `Loja:UrlBase` e emita uma licença de teste com esse domínio e `--modulo whatsapp`.
Para a API oficial, o app da Meta em modo de desenvolvimento oferece um número de teste e até 5 números de destino.

## Limitações conhecidas

- Desconto no Pix e cupons ainda não existem no portal (backlog).
- Cancelamento e troca são feitos pelo atendente ou pelo site.
- Número do WhatsApp diferente do telefone do cadastro do site cria uma conta separada (por segurança).
