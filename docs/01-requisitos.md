# 01 — Requisitos do Portal de Vendas

## 1. Visão do produto

Loja virtual de um único vendedor, construída em ASP.NET Core MVC com SQL Server, para vender produtos diversos pela internet. O cliente navega pelo catálogo, coloca produtos no carrinho, informa o endereço de entrega (com preenchimento automático pelo CEP), vê o valor do frete e paga por Pix, cartão de crédito ou boleto via Mercado Pago. O vendedor administra produtos, estoque, pedidos e frete por um painel administrativo.

## 2. Personas

| Persona | Descrição | Objetivo principal |
|---|---|---|
| **Visitante** | Pessoa navegando sem login | Encontrar e comparar produtos |
| **Cliente** | Visitante cadastrado e autenticado | Comprar com rapidez e segurança e acompanhar o pedido |
| **Administrador** | Dono da loja ou funcionário | Manter catálogo e estoque, processar pedidos, ver vendas |

## 3. Requisitos funcionais

Prioridade MoSCoW: **M** = Must (MVP), **S** = Should, **C** = Could.

### Catálogo

**RF01 (M) — Listar produtos**
Como visitante, quero ver os produtos ativos em uma vitrine paginada, para escolher o que comprar.
- Dado que existem 30 produtos ativos, quando acesso a página inicial, então vejo 12 por página, com foto, nome e preço, e controles de paginação.
- Dado que um produto está inativo, quando acesso a vitrine, então ele não aparece.
- Dado que um produto tem estoque zero, quando acesso a vitrine, então ele aparece marcado como "Esgotado" e sem botão de compra.

**RF02 (M) — Buscar e filtrar**
Como visitante, quero buscar por nome e filtrar por categoria e faixa de preço, para achar o produto rápido.
- Dado o termo "fone", quando busco, então vejo apenas produtos ativos cujo nome ou descrição contém "fone", sem diferenciar maiúsculas e acentos.
- Dado que filtro a categoria "Eletrônicos" e preço até R$ 200, então vejo só produtos que atendem aos dois filtros.
- Dado que ordeno por "menor preço", então a lista vem em ordem crescente de preço.

**RF03 (M) — Detalhe do produto**
Como visitante, quero ver a página do produto com fotos, descrição, preço, estoque disponível e dimensões, para decidir a compra.
- Dado um produto ativo, quando abro sua página, então vejo todas as fotos, a descrição e o preço.
- Dado um produto inexistente ou inativo, quando acesso sua URL, então recebo a página 404.

### Carrinho

**RF04 (M) — Carrinho de compras**
Como visitante, quero adicionar, alterar quantidade e remover itens do carrinho, para montar minha compra.
- Dado um produto com estoque 5, quando tento adicionar 6 unidades, então vejo a mensagem "Quantidade indisponível" e a quantidade fica em 5.
- Dado que adiciono itens sem estar logado, quando faço login, então os itens do carrinho anônimo são mesclados ao carrinho da minha conta.
- Dado um item no carrinho cujo preço mudou, quando abro o carrinho, então vejo o preço atual e um aviso de alteração.

### Conta do cliente

**RF05 (M) — Cadastro e login**
Como visitante, quero criar conta com nome, e-mail, CPF, telefone e senha, para poder comprar.
- Dado um CPF com dígitos verificadores inválidos, quando envio o cadastro, então vejo "CPF inválido".
- Dado um e-mail já cadastrado, quando tento me cadastrar, então vejo uma mensagem genérica que não confirma se o e-mail existe.
- Dado que erro a senha 5 vezes seguidas, então a conta fica bloqueada por 15 minutos.
- Dado um cadastro concluído, então recebo e-mail de confirmação e só posso finalizar compras após confirmar.

**RF06 (S) — Recuperar senha**
Como cliente, quero redefinir a senha por e-mail, para recuperar o acesso.
- Dado que peço a redefinição, então recebo um link válido por 1 hora e de uso único.

### Endereço e frete

**RF07 (M) — Endereços de entrega**
Como cliente, quero cadastrar, editar e escolher endereços de entrega, para receber em casa ou no trabalho.
- Dado que digito um CEP válido, quando saio do campo, então rua, bairro, cidade e UF são preenchidos automaticamente (ViaCEP); número e complemento continuam editáveis.
- Dado um CEP inexistente, então vejo "CEP não encontrado" e posso preencher manualmente.
- Dado que tenho vários endereços, então posso marcar um como padrão, que já vem selecionado no checkout.

**RF08 (M) — Cálculo de frete por peso e dimensões**
Como cliente, quero ver o valor e o prazo real de entrega para o meu CEP antes de pagar, para saber o custo total.
- Dado um carrinho e um CEP, quando calculo o frete, então o sistema cota em tempo real no Melhor Envio (Correios PAC, SEDEX, Jadlog e outras transportadoras habilitadas), usando o peso, as dimensões e o valor declarado de cada item, e mostra cada opção com transportadora, valor e prazo em dias úteis.
- Dado que o administrador configurou dias de manuseio (tempo de separação e postagem), então esse número é somado ao prazo de cada opção.
- Dado que o subtotal é igual ou maior que o valor de frete grátis configurado, então a opção mais barata aparece com valor R$ 0,00.
- Dado que a API do Melhor Envio está indisponível, então o sistema usa a tabela de contingência por faixa de CEP e registra o incidente em log.
- Dado um CEP que nenhuma transportadora nem faixa de contingência atende, então vejo "Não entregamos neste CEP" e não consigo finalizar.
- Dado que o cliente escolheu uma opção de frete, quando confirma o pedido, então o servidor recota e usa o valor recalculado (o valor nunca vem do navegador).

### Checkout e pagamento

**RF09 (M) — Finalizar pedido**
Como cliente, quero revisar itens, endereço, frete e total, e escolher a forma de pagamento, para concluir a compra.
- Dado um carrinho válido, quando confirmo o pedido, então ele é criado com status "Aguardando pagamento" e o estoque dos itens fica reservado.
- Dado que outro cliente comprou a última unidade enquanto eu estava no checkout, quando confirmo, então o pedido não é criado e vejo quais itens ficaram indisponíveis.
- Dado que clico duas vezes em "Confirmar", então só um pedido é criado (idempotência).

**RF10 (M) — Pagar com Pix**
Como cliente, quero pagar com Pix por QR Code ou código copia-e-cola, para pagar na hora.
- Dado que escolho Pix, então vejo o QR Code e o código copia-e-cola com validade de 30 minutos.
- Dado que o Mercado Pago confirma o pagamento, então o pedido passa para "Pago" automaticamente e a página atualiza sem eu recarregar.
- Dado que o Pix expira sem pagamento, então o pedido é cancelado e o estoque reservado é liberado.

**RF11 (M) — Pagar com cartão de crédito**
Como cliente, quero pagar com cartão de crédito em até 12 vezes, para parcelar a compra.
- Dado que preencho o cartão, então os dados são tokenizados no navegador pelo Mercado Pago e nunca chegam ao nosso servidor.
- Dado um pagamento aprovado, então o pedido vai para "Pago".
- Dado um pagamento recusado, então vejo o motivo em linguagem simples (por exemplo, "saldo insuficiente") e posso tentar outro cartão ou outra forma de pagamento.
- Dado um pagamento "em análise", então o pedido fica "Pagamento em análise" até a resposta final do Mercado Pago.

**RF12 (M) — Pagar com boleto**
Como cliente, quero gerar um boleto, para pagar no banco ou no app.
- Dado que escolho boleto, então vejo a linha digitável e o link para o PDF, com vencimento em 3 dias úteis.
- Dado que o boleto vence sem pagamento, então o pedido é cancelado e o estoque reservado é liberado.

**RF13 (M) — Confirmação via webhook**
Como sistema, quero receber as notificações do Mercado Pago, para atualizar o status do pagamento sem depender do navegador do cliente.
- Dado uma notificação com assinatura inválida, então ela é rejeitada e registrada em log.
- Dado a mesma notificação recebida duas vezes, então o pedido é atualizado uma única vez.
- Dado uma notificação válida, então o sistema consulta o pagamento na API do Mercado Pago (não confia no corpo da notificação) antes de mudar o status.

### Pós-venda

**RF14 (M) — Meus pedidos**
Como cliente, quero ver meus pedidos com status, itens, valores, endereço e código de rastreio, para acompanhar a entrega.
- Dado que estou logado, então vejo só os meus pedidos; ao tentar abrir o pedido de outro cliente pela URL, recebo 404.
- Dado um pedido "Aguardando pagamento" via Pix ou boleto, então posso reabrir o QR Code ou o boleto.

**RF15 (S) — E-mails transacionais**
Como cliente, quero receber e-mails a cada mudança de status (pedido criado, pago, enviado, entregue, cancelado), para ficar informado.

**RF16 (S) — Cancelar pedido**
Como cliente, quero cancelar um pedido ainda não enviado, para desistir da compra.
- Dado um pedido pago e não enviado, quando cancelo, então o reembolso é solicitado ao Mercado Pago e o estoque é devolvido.
- Dado um pedido já enviado, então a opção de cancelar não aparece.

### Painel administrativo

**RF17 (M) — Gerenciar produtos e categorias**
Como administrador, quero cadastrar, editar, ativar e desativar produtos e categorias, com fotos, preço, estoque, peso e dimensões.
- Dado um upload que não seja JPG, PNG ou WebP, ou que tenha mais de 2 MB, então ele é recusado.
- Dado um produto que já teve vendas, quando tento excluir, então ele é apenas desativado, para preservar o histórico.

**RF18 (M) — Gerenciar pedidos**
Como administrador, quero listar e filtrar pedidos por status e período e mudar o status para "Em separação", "Enviado" (com código de rastreio) e "Entregue".
- Dado um pedido "Aguardando pagamento", então não consigo marcá-lo como "Enviado".
- Dado cada mudança de status, então fica registrado quem mudou, quando e de qual status para qual.

**RF19 (M) — Configurar frete**
Como administrador, quero configurar o CEP de origem, os dias de manuseio, o valor mínimo para frete grátis e a tabela de contingência por faixa de CEP.
- Dado um produto sem peso ou dimensões válidos, então o cadastro é recusado (peso > 0, altura, largura e comprimento > 0).

**RF20 (S) — Painel de vendas**
Como administrador, quero ver faturamento, número de pedidos, ticket médio e produtos mais vendidos por período.

**RF21 (C) — Cupons de desconto**
Como administrador, quero criar cupons de desconto (percentual ou valor fixo) com validade e limite de uso.

**RF22 (C) — Geração de etiquetas de envio**
Comprar e imprimir a etiqueta do Melhor Envio direto do painel ao marcar o pedido como "Em separação", preenchendo o código de rastreio automaticamente.

## 4. Regras de negócio

| Código | Regra |
|---|---|
| RN01 | O preço do produto é gravado no item do pedido no momento da compra; alterações posteriores de preço não afetam pedidos existentes. |
| RN02 | O estoque é reservado na criação do pedido e baixado na confirmação do pagamento. Pedidos cancelados ou expirados devolvem o estoque. |
| RN03 | Estoque nunca pode ficar negativo; a reserva é feita em transação com verificação no banco. |
| RN04 | Prazos: Pix expira em 30 minutos; boleto vence em 3 dias úteis; pedido não pago é cancelado automaticamente após o prazo. |
| RN05 | Total do pedido = soma de (preço unitário × quantidade) + frete − desconto. Valores calculados sempre no servidor, nunca recebidos do navegador. |
| RN06 | Parcelamento no cartão de 1 a 12 vezes; parcela mínima de R$ 10,00; juros conforme configuração do Mercado Pago. |
| RN07 | Fluxo de status do pedido: Aguardando pagamento → Pago (ou Pagamento em análise → Pago) → Em separação → Enviado → Entregue. Cancelado é possível até "Em separação". Nenhuma outra transição é permitida. |
| RN08 | Status "Pago" só é atribuído após consulta direta à API do Mercado Pago confirmando `approved`. |
| RN09 | Quantidade máxima por item no carrinho = estoque disponível, limitada a 10 unidades por produto. |
| RN10 | CPF é obrigatório para comprar (exigência do Mercado Pago para boleto e emissão de nota fiscal). |

## 5. Requisitos não funcionais

| Código | Categoria | Requisito |
|---|---|---|
| RNF01 | Desempenho | Páginas de vitrine e produto respondem em até 1 s (p95) com 100 usuários simultâneos e 10 mil produtos. |
| RNF02 | Segurança | Senhas com hash PBKDF2 (`PasswordHasher` do ASP.NET Core); HTTPS obrigatório com HSTS; proteção CSRF em todo formulário; consultas parametrizadas via EF Core. |
| RNF03 | Segurança / PCI | Dados de cartão nunca trafegam nem são gravados no servidor; tokenização no navegador pelo Mercado Pago (escopo PCI SAQ A). |
| RNF04 | Segurança | Segredos (Access Token do Mercado Pago, string de conexão, chave do webhook) fora do repositório, em variáveis de ambiente ou cofre. |
| RNF05 | Segurança | Painel administrativo acessível só ao perfil Administrador, com autenticação em dois fatores obrigatória. |
| RNF06 | Segurança | Rate limiting em login, cadastro, cálculo de frete e webhook. |
| RNF07 | LGPD | Consentimento explícito nos termos e na política de privacidade; cliente pode exportar e solicitar exclusão dos dados pessoais (anonimização preservando pedidos para obrigações fiscais). CPF e telefone mascarados em telas e logs. |
| RNF08 | Auditoria | Toda mudança de status de pedido e de pagamento registrada com data, autor e valores anterior e novo. |
| RNF09 | Observabilidade | Logs estruturados (Serilog) com ID de correlação; health check em `/health`; erros nunca exibem detalhes técnicos ao usuário. |
| RNF10 | Disponibilidade | Meta de 99,5% ao mês; backup completo diário e de log a cada 15 minutos. |
| RNF11 | Usabilidade | Layout responsivo (celular primeiro); checkout em no máximo 3 etapas. |
| RNF12 | Acessibilidade | Contraste e navegação por teclado conforme WCAG 2.1 nível AA nas telas de compra. |
| RNF13 | Manutenibilidade | Cobertura de testes de pelo menos 80% nas regras de negócio (camadas de domínio e aplicação). |

## 6. Fora do escopo do MVP

- Marketplace com múltiplos vendedores e repasses
- Emissão de nota fiscal eletrônica (integração futura com um emissor de NF-e)
- Aplicativo móvel nativo
- Integração com ERP ou sistema de estoque externo
- Avaliações e comentários de produtos
- Produtos com variações (tamanho, cor) — cada variação será cadastrada como produto separado no MVP
- Programa de fidelidade e cashback
- Carteira digital do Mercado Pago, débito virtual e Pix parcelado

## 7. Premissas confirmadas

1. **Hospedagem:** servidor Windows próprio com IIS e SQL Server (confirmado).
2. **Frete:** cotação em tempo real por peso e dimensões via API do Melhor Envio, com tabela de contingência por faixa de CEP (confirmado).
3. **Compilação e testes:** na máquina do desenvolvedor, em `C:\SRC\PortalVendas` (confirmado).
4. **Volume inicial (suposição):** até 100 usuários simultâneos, até 10 mil produtos e até 2 mil pedidos por mês.
5. **E-mail (suposição):** envio por SMTP de um provedor transacional (SendGrid, Amazon SES, Brevo) ou do servidor de e-mail da empresa.
6. **Imagens (suposição):** armazenadas em uma pasta do servidor fora da pasta publicada do site, para não serem apagadas a cada deploy.
