// Pagamento com cartão: Card Payment Brick do Mercado Pago.
// O cartão é digitado em campos seguros do Mercado Pago e vira um token; o servidor recebe só o token.
(function () {
    'use strict';

    const container = document.getElementById('cartao-brick');
    if (!container || typeof MercadoPago === 'undefined') return;

    const mensagem = document.getElementById('cartao-mensagem');
    const mostrar = (texto, tipo) => {
        mensagem.className = `alert alert-${tipo}`;
        mensagem.textContent = texto;
        mensagem.scrollIntoView({ behavior: 'smooth', block: 'center' });
    };

    const mp = new MercadoPago(container.dataset.publicKey, { locale: 'pt-BR' });
    const valor = parseFloat(container.dataset.valor);

    mp.bricks().create('cardPayment', 'cartao-brick', {
        initialization: {
            amount: valor,
            payer: { email: container.dataset.email }
        },
        customization: {
            paymentMethods: {
                maxInstallments: parseInt(container.dataset.parcelasMax, 10) || 1,
                types: { excluded: ['debit_card', 'prepaid_card'] }
            },
            visual: { style: { theme: 'default' } }
        },
        callbacks: {
            onReady: () => { },
            onSubmit: (dados) => new Promise((resolver, rejeitar) => {
                mensagem.className = 'alert d-none';
                fetch(container.dataset.url, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Accept': 'application/json',
                        'RequestVerificationToken': container.dataset.token
                    },
                    credentials: 'same-origin',
                    body: JSON.stringify(dados)
                })
                    .then((r) => {
                        if (r.status === 401) throw new Error('Sua sessão expirou. Entre de novo para pagar.');
                        if (!r.ok) throw new Error('Não foi possível processar o pagamento. Tente novamente.');
                        return r.json();
                    })
                    .then((r) => {
                        if (r.sucesso) {
                            resolver();
                            mostrar(r.mensagem, r.aprovado ? 'success' : 'info');
                            setTimeout(() => { window.location.href = r.redirecionar; }, 1500);
                        } else {
                            mostrar(r.mensagem, 'danger');
                            rejeitar();
                        }
                    })
                    .catch((erro) => {
                        mostrar(erro.message, 'danger');
                        rejeitar();
                    });
            }),
            onError: (erro) => {
                console.error('Card Payment Brick', erro);
                mostrar('Não foi possível carregar o formulário do cartão. Recarregue a página ou use Pix/boleto.', 'warning');
            }
        }
    });
})();
