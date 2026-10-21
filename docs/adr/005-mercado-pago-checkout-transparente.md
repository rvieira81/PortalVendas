# ADR-005: Mercado Pago — API de pagamentos (Checkout Transparente) + Card Payment Brick

**Contexto:** Pix, cartão de crédito parcelado e boleto, com o cliente sem sair do site e sem que o servidor toque em dados de cartão.

**Opções consideradas:** (a) Checkout Pro (redireciona para o site do Mercado Pago); (b) Checkout Transparente via API REST + Card Payment Brick para o cartão; (c) SDK .NET oficial do Mercado Pago.

**Decisão:** (b), chamando a API REST com `HttpClient` tipado. O Brick tokeniza o cartão no navegador; o servidor recebe só token, bandeira, emissor e parcelas.

**Consequências:** escopo PCI mínimo (SAQ A). Nenhuma dependência de SDK de terceiro, e cada chamada fica visível no código e no log. O Content-Security-Policy precisa liberar os domínios do Mercado Pago (`*.mercadopago.com`, `*.mercadolibre.com`, `*.mlstatic.com`). Pix e boleto expiram e são conciliados pelo job (ADR-007).
