// PortalVendas — comportamentos de página. Sem dependências; tudo via atributos data-*.
(function () {
    'use strict';

    const digitos = (v) => (v || '').replace(/\D/g, '');

    // ------------------------------------------------------------ máscaras simples
    const mascaras = {
        cpf: (v) => digitos(v).slice(0, 11)
            .replace(/(\d{3})(\d)/, '$1.$2')
            .replace(/(\d{3})(\d)/, '$1.$2')
            .replace(/(\d{3})(\d{1,2})$/, '$1-$2'),
        cep: (v) => digitos(v).slice(0, 8).replace(/(\d{5})(\d)/, '$1-$2'),
        telefone: (v) => {
            const d = digitos(v).slice(0, 11);
            if (d.length <= 10) return d.replace(/(\d{2})(\d)/, '($1) $2').replace(/(\d{4})(\d)/, '$1-$2');
            return d.replace(/(\d{2})(\d)/, '($1) $2').replace(/(\d{5})(\d)/, '$1-$2');
        }
    };
    document.querySelectorAll('[data-mascara]').forEach((campo) => {
        const aplicar = mascaras[campo.dataset.mascara];
        if (!aplicar) return;
        campo.addEventListener('input', () => { campo.value = aplicar(campo.value); });
        campo.value = aplicar(campo.value);
    });

    // ------------------------------------------------------------ CEP → endereço (ViaCEP via /api/cep)
    document.querySelectorAll('[data-cep]').forEach((campo) => {
        const form = campo.closest('form');
        const status = form.querySelector('[data-cep-status]');
        let ultimo = '';
        const consultar = async () => {
            const cep = digitos(campo.value);
            if (cep.length !== 8 || cep === ultimo) return;
            ultimo = cep;
            if (status) status.textContent = 'Buscando endereço…';
            try {
                const resp = await fetch(`/api/cep/${cep}`, { headers: { Accept: 'application/json' } });
                if (!resp.ok) {
                    if (status) status.textContent = 'CEP não encontrado. Preencha o endereço manualmente.';
                    return;
                }
                const e = await resp.json();
                const preencher = (nome, valor) => {
                    const alvo = form.querySelector(`[data-campo-cep="${nome}"]`);
                    if (alvo && valor) alvo.value = valor;
                };
                preencher('logradouro', e.logradouro);
                preencher('bairro', e.bairro);
                preencher('cidade', e.cidade);
                preencher('uf', e.uf);
                if (status) status.textContent = 'Endereço encontrado. Confira e informe o número.';
                const numero = form.querySelector('[name="Numero"]');
                if (numero && !numero.value) numero.focus();
            } catch {
                if (status) status.textContent = 'Não foi possível consultar o CEP agora. Preencha manualmente.';
            }
        };
        campo.addEventListener('blur', consultar);
        campo.addEventListener('input', () => { if (digitos(campo.value).length === 8) consultar(); });
    });

    // ------------------------------------------------------------ galeria do produto
    const fotoPrincipal = document.getElementById('foto-principal');
    document.querySelectorAll('.miniatura[data-foto]').forEach((m) => m.addEventListener('click', () => {
        if (!fotoPrincipal) return;
        fotoPrincipal.src = m.dataset.foto;
        document.querySelectorAll('.miniatura').forEach((x) => x.classList.toggle('ativa', x === m));
    }));

    // ------------------------------------------------------------ navegação e envio automático
    document.querySelectorAll('select[data-navegar]').forEach((s) =>
        s.addEventListener('change', () => { window.location.href = s.value; }));

    document.querySelectorAll('[data-enviar-ao-mudar]').forEach((s) =>
        s.addEventListener('change', () => s.form && s.form.submit()));

    // Checkout: trocar o endereço recota o frete
    document.querySelectorAll('input[data-recotar]').forEach((r) =>
        r.addEventListener('change', () => { window.location.href = r.dataset.recotar; }));

    // Checkout: atualiza o total ao escolher o frete (o valor final é sempre recalculado no servidor)
    const total = document.querySelector('[data-resumo-total]');
    const resumoFrete = document.querySelector('[data-resumo-frete]');
    if (total && resumoFrete) {
        const moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
        const subtotal = parseFloat(total.dataset.subtotal);
        document.querySelectorAll('input[data-valor-frete]').forEach((r) => r.addEventListener('change', () => {
            const frete = parseFloat(r.dataset.valorFrete);
            resumoFrete.textContent = frete === 0 ? 'Grátis' : moeda.format(frete);
            total.textContent = moeda.format(subtotal + frete);
        }));
    }

    // ------------------------------------------------------------ confirmação e envio único
    document.querySelectorAll('form[data-confirmar]').forEach((f) =>
        f.addEventListener('submit', (ev) => { if (!window.confirm(f.dataset.confirmar)) ev.preventDefault(); }));

    document.querySelectorAll('[data-enviar-uma-vez]').forEach((b) => {
        const form = b.closest('form');
        if (!form) return;
        form.addEventListener('submit', () => {
            // Evita o duplo clique; o servidor também é idempotente
            setTimeout(() => { b.disabled = true; b.textContent = 'Aguarde…'; }, 0);
        });
    });

    // ------------------------------------------------------------ copiar para a área de transferência
    document.querySelectorAll('[data-copiar]').forEach((b) => b.addEventListener('click', async () => {
        const alvo = document.querySelector(b.dataset.copiar);
        if (!alvo) return;
        try {
            await navigator.clipboard.writeText(alvo.value);
        } catch {
            alvo.select();
            document.execCommand('copy');
        }
        const original = b.textContent;
        b.textContent = 'Copiado!';
        setTimeout(() => { b.textContent = original; }, 2000);
    }));

    // ------------------------------------------------------------ acompanhar o pagamento (Pix/boleto)
    const acompanhar = document.querySelector('[data-acompanhar-status]');
    if (acompanhar) {
        let tentativas = 0;
        const verificar = async () => {
            tentativas++;
            try {
                const resp = await fetch(acompanhar.dataset.acompanharStatus, { headers: { Accept: 'application/json' } });
                if (resp.ok) {
                    const r = await resp.json();
                    if (r.pago) {
                        acompanhar.className = 'alert alert-success small mb-0';
                        acompanhar.textContent = 'Pagamento confirmado! Redirecionando…';
                        window.location.href = acompanhar.dataset.destino;
                        return;
                    }
                }
            } catch { /* tenta de novo no próximo ciclo */ }
            // A cada 5 s por 30 minutos; depois, a cada 30 s
            setTimeout(verificar, tentativas < 360 ? 5000 : 30000);
        };
        setTimeout(verificar, 5000);
    }
})();
