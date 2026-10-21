# 07 — Manual de uso

## Cliente

1. **Comprar sem conta:** navegue, busque e adicione ao carrinho. Para finalizar é preciso entrar ou criar conta — o carrinho é mantido.
2. **Criar conta:** nome completo, e-mail, CPF, celular e senha (8+ caracteres, letras e números). Confirme pelo link enviado por e-mail.
3. **Endereço:** digite o CEP e o sistema preenche rua, bairro e cidade; complete com número e complemento. Você pode ter até 10 endereços e escolher um padrão.
4. **Entrega:** no checkout aparecem as transportadoras que atendem o seu CEP, com valor e prazo. Trocar o endereço recalcula o frete.
5. **Pagamento:**
   - **Pix** — QR Code ou copia e cola, válido por 30 minutos. A página confirma sozinha quando o Pix cai.
   - **Cartão de crédito** — até 12x (conforme o valor). Se for recusado, o motivo aparece e você pode tentar outro cartão ou Pix.
   - **Boleto** — vence em 3 dias úteis; a confirmação leva até 3 dias úteis depois do pagamento.
6. **Acompanhar:** em *Meus pedidos* aparecem o status, o histórico e o código de rastreio. Cada mudança também chega por e-mail.
7. **Cancelar:** enquanto o pedido não foi enviado, pelo botão *Cancelar pedido*. Se já estava pago, o estorno é solicitado na hora (no cartão, aparece na fatura em até 2 faturas; Pix volta para a conta).
8. **Privacidade:** em *Minha conta → Privacidade e dados* é possível baixar seus dados e excluir a conta.

## Administrador

**Acesso:** `/conta/entrar` com o e-mail de administrador. No primeiro acesso, configure o aplicativo autenticador (Google/Microsoft Authenticator); depois, todo login pede o código de 6 dígitos.

| Tela | Para quê |
|---|---|
| **Painel** | Faturamento, pedidos pagos, ticket médio, mais vendidos e o que precisa de ação (separar, enviar) |
| **Pedidos** | Filtrar por status, período, número, nome ou e-mail. No detalhe: itens, pagamentos (com id do Mercado Pago), histórico completo e botões do próximo passo |
| **Produtos** | Cadastro com preço, preço "de", estoque e **peso e medidas da embalagem** (obrigatórios: são usados no frete). Até 8 fotos; a primeira é a da vitrine |
| **Categorias** | Criar, renomear e desativar |
| **Frete e pagamento** | CEP de origem, dias de manuseio, frete grátis, parcelas máximas e parcela mínima; tabela de contingência por faixa de CEP |

**Rotina de expedição:**
1. Pedido **Pago** → abra o pedido → *Marcar como "Em separação"*.
2. Gere a etiqueta no painel do Melhor Envio (ou na agência) → *Marcar como "Enviado"* com o código de rastreio.
3. Quando entregue → *Marcar como "Entregue"*.

**Estoque:** "Estoque" é o físico; "Reservado" são unidades presas em pedidos aguardando pagamento. O site vende só a diferença. Pedidos não pagos são cancelados automaticamente (Pix em ~45 minutos, boleto alguns dias após o vencimento) e a reserva volta.

**Excluir produto:** se o produto já teve vendas, ele é apenas desativado (sai da loja e o histórico fica).

**Alertas:** se um pedido mostrar a faixa vermelha "Há um alerta no histórico", houve estorno ou contestação no Mercado Pago — confira antes de enviar.
