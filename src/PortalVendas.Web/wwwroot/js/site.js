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

    // ------------------------------------------------------------ mini carrinho (gaveta após "Adicionar ao carrinho")
    const mini = document.getElementById('mini-carrinho');
    if (mini) {
        const fundo = document.querySelector('.mini-fundo');
        const anterior = document.activeElement;
        document.body.classList.add('mini-aberto');
        mini.focus();
        const fechar = () => {
            mini.hidden = true;
            if (fundo) fundo.hidden = true;
            document.body.classList.remove('mini-aberto');
            document.removeEventListener('keydown', teclado);
            if (anterior && anterior.focus) anterior.focus();
        };
        const teclado = (ev) => {
            if (ev.key === 'Escape') { fechar(); return; }
            if (ev.key !== 'Tab') return;
            // mantém o foco dentro da gaveta enquanto ela está aberta
            const focaveis = mini.querySelectorAll('a[href], button:not([disabled])');
            if (focaveis.length === 0) return;
            const primeiro = focaveis[0], ultimo = focaveis[focaveis.length - 1];
            if (ev.shiftKey && document.activeElement === primeiro) { ev.preventDefault(); ultimo.focus(); }
            else if (!ev.shiftKey && document.activeElement === ultimo) { ev.preventDefault(); primeiro.focus(); }
        };
        document.addEventListener('keydown', teclado);
        document.querySelectorAll('[data-mini-fechar]').forEach((b) => b.addEventListener('click', fechar));
    }

    // ------------------------------------------------------------ mostrar/ocultar (tabela de parcelas)
    document.querySelectorAll('[data-alternar]').forEach((b) => b.addEventListener('click', () => {
        const alvo = document.getElementById(b.dataset.alternar);
        if (!alvo) return;
        alvo.hidden = !alvo.hidden;
        b.setAttribute('aria-expanded', String(!alvo.hidden));
        b.textContent = alvo.hidden ? 'Ver todas as parcelas' : 'Ocultar parcelas';
    }));

    // ------------------------------------------------------------ botões − / + da quantidade
    document.querySelectorAll('[data-qtd]').forEach((b) => b.addEventListener('click', () => {
        const campo = b.parentElement.querySelector('input[type="number"]');
        if (!campo) return;
        const min = parseInt(campo.min || '1', 10), max = parseInt(campo.max || '99', 10);
        const atual = parseInt(campo.value || '1', 10) || 1;
        campo.value = Math.min(max, Math.max(min, atual + parseInt(b.dataset.qtd, 10)));
    }));

    // ------------------------------------------------------------ calculadora de frete da página do produto
    document.querySelectorAll('[data-frete-produto]').forEach((caixa) => {
        const campo = caixa.querySelector('input');
        const botao = caixa.querySelector('[data-calcular-frete]');
        const saida = caixa.querySelector('.pp-frete-resultado');
        const quantidade = document.getElementById('quantidade');
        const escapar = (t) => String(t).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
        try { const salvo = localStorage.getItem('pv-cep'); if (salvo && !campo.value) campo.value = mascaras.cep(salvo); } catch { /* sem armazenamento */ }

        const calcular = async () => {
            const cep = digitos(campo.value);
            if (cep.length !== 8) { saida.innerHTML = '<span class="pp-frete-erro">Digite um CEP com 8 números.</span>'; return; }
            botao.disabled = true;
            saida.innerHTML = '<span class="text-secondary small">Consultando transportadoras…</span>';
            try {
                const q = new URLSearchParams({ produtoId: caixa.dataset.freteProduto, cep, quantidade: quantidade ? quantidade.value : '1' });
                const resp = await fetch(`/api/frete?${q}`, { headers: { Accept: 'application/json' } });
                const dados = await resp.json().catch(() => null);
                if (!resp.ok) {
                    saida.innerHTML = `<span class="pp-frete-erro">${escapar((dados && dados.mensagem) || (resp.status === 429 ? 'Muitas consultas seguidas. Tente em instantes.' : 'Não foi possível calcular agora.'))}</span>`;
                    return;
                }
                try { localStorage.setItem('pv-cep', cep); } catch { /* sem armazenamento */ }
                saida.innerHTML = dados.length === 0
                    ? '<span class="pp-frete-erro">Nenhuma opção de entrega para este CEP.</span>'
                    : dados.map((o) => `<div class="pp-frete-opcao"><span><strong>${escapar(o.descricao)}</strong><small>Chega em até ${escapar(o.prazo)}</small></span><strong>${escapar(o.valorTexto)}</strong></div>`).join('');
            } catch {
                saida.innerHTML = '<span class="pp-frete-erro">Não foi possível calcular agora. Tente de novo.</span>';
            } finally {
                botao.disabled = false;
            }
        };
        botao.addEventListener('click', calcular);
        campo.addEventListener('keydown', (ev) => { if (ev.key === 'Enter') { ev.preventDefault(); calcular(); } });
    });

    // ------------------------------------------------------------ painel: cores da identidade da loja
    document.querySelectorAll('[data-cor-para]').forEach((b) => b.addEventListener('click', () => {
        const campo = document.getElementById(b.dataset.corPara);
        if (!campo) return;
        campo.value = b.dataset.cor;
        campo.dispatchEvent(new Event('input'));
    }));
    const previa = document.getElementById('previa-marca');
    document.querySelectorAll('input[data-previa]').forEach((c) => c.addEventListener('input', () => {
        if (previa) previa.style.setProperty(c.dataset.previa, c.value);
    }));

    // ------------------------------------------------------------ painel WhatsApp: provedor, histórico e atualização
    const radiosProvedor = document.querySelectorAll('input[name="Provedor"]');
    if (radiosProvedor.length) {
        const ajustar = () => radiosProvedor.forEach((r) => {
            if (!r.dataset.mostrar) return;
            const bloco = document.getElementById(r.dataset.mostrar);
            if (bloco) bloco.hidden = !r.checked;
        });
        radiosProvedor.forEach((r) => r.addEventListener('change', ajustar));
        ajustar();
    }
    const chat = document.querySelector('[data-atualizar]');
    if (chat) {
        chat.scrollTop = chat.scrollHeight;
        const resposta = document.getElementById('texto');
        const segundos = parseInt(chat.dataset.atualizar, 10) || 15;
        // Recarrega para mostrar mensagens novas, sem atrapalhar quem está digitando
        setInterval(() => {
            if (resposta && (resposta.value.trim() !== '' || document.activeElement === resposta)) return;
            window.location.reload();
        }, segundos * 1000);
    }

    // Histórico de conversa: começa mostrando a mensagem mais recente
    document.querySelectorAll('[data-rolar-fim]').forEach((e) => { e.scrollTop = e.scrollHeight; });
})();
