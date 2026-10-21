# ADR-001: Monólito modular em camadas

**Contexto:** loja de um vendedor, ~100 usuários simultâneos, time pequeno, hospedagem em um servidor IIS próprio.

**Opções consideradas:** (a) monólito em camadas; (b) microsserviços (catálogo, pedidos, pagamentos); (c) projeto único sem camadas.

**Decisão:** (a). Quatro projetos — Domain, Application, Infrastructure, Web — publicados como um único site.

**Consequências:** um só deploy e um só banco, transações locais simples para estoque e pedido. As integrações ficam atrás de interfaces, o que permite trocar Mercado Pago ou Melhor Envio sem tocar nas regras. Se um módulo precisar escalar sozinho no futuro, ele já tem fronteira definida.
