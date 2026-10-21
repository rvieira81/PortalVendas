using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Pagamentos;
using PortalVendas.Domain.Pagamentos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Tests.Apoio;

namespace PortalVendas.Tests.Aplicacao;

public class PagamentoServiceTests
{
    private static NotificacaoRecebida Webhook(long gatewayId, string requestId = "req-1", bool assinaturaValida = true) =>
        new(requestId, "payment", gatewayId.ToString(), "{}", assinaturaValida);

    [Fact]
    public async Task Pix_gerado_com_validade_de_30_minutos()   // RF10
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();

        var r = await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId);

        Assert.True(r.Sucesso, r.Erro);
        Assert.Equal(StatusPagamento.Pendente, r.Valor!.Status);
        Assert.NotNull(r.Valor.PixQrCode);
        Assert.Equal(c.Relogio.AgoraUtc.AddMinutes(30), r.Valor.ExpiraEm);
        var pedido = await c.RecarregarPedidoAsync(pedidoId);
        Assert.Equal(c.Relogio.AgoraUtc.AddMinutes(30), pedido.ExpiraEm);
    }

    [Fact]
    public async Task Reaproveita_pix_vigente_em_vez_de_gerar_outro()
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();

        var r1 = await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId);
        var r2 = await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId);

        Assert.Equal(r1.Valor!.Id, r2.Valor!.Id);
        Assert.Single(c.Gateway.Pagamentos);
    }

    [Fact]
    public async Task Webhook_aprovado_confirma_pedido_e_baixa_estoque()   // RF13, RN02, RN08
    {
        using var c = new Cenario();
        var (cliente, produto, pedidoId) = await c.CriarPedidoAsync(quantidade: 2, estoque: 10);
        var pix = (await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId)).Valor!;
        c.Gateway.Aprovar(pix.GatewayId!.Value);

        await c.Pagamentos.ProcessarNotificacaoAsync(Webhook(pix.GatewayId.Value));

        var pedido = await c.RecarregarPedidoAsync(pedidoId);
        Assert.Equal(StatusPedido.Pago, pedido.Status);
        Assert.NotNull(pedido.PagoEm);
        Assert.Null(pedido.ExpiraEm);
        var p = await c.RecarregarProdutoAsync(produto.Id);
        Assert.Equal(8, p.Estoque);
        Assert.Equal(0, p.EstoqueReservado);
        Assert.Contains(c.Email.Enviados, e => e.Assunto.Contains("Pagamento confirmado"));
    }

    [Fact]
    public async Task Webhook_repetido_e_processado_uma_vez()   // RF13
    {
        using var c = new Cenario();
        var (cliente, produto, pedidoId) = await c.CriarPedidoAsync(quantidade: 1, estoque: 5);
        var pix = (await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId)).Valor!;
        c.Gateway.Aprovar(pix.GatewayId!.Value);

        await c.Pagamentos.ProcessarNotificacaoAsync(Webhook(pix.GatewayId.Value, "req-x"));
        await c.Pagamentos.ProcessarNotificacaoAsync(Webhook(pix.GatewayId.Value, "req-x"));
        await c.Pagamentos.ProcessarNotificacaoAsync(Webhook(pix.GatewayId.Value, "req-y"));   // outro envio do mesmo evento

        Assert.Equal(2, c.Gateway.Consultas);   // req-x só uma vez; req-y consulta, mas não muda nada
        Assert.Equal(4, (await c.RecarregarProdutoAsync(produto.Id)).Estoque);   // baixou uma vez só
        Assert.Equal(1, (await c.RecarregarPedidoAsync(pedidoId)).Historico.Count(h => h.StatusNovo == StatusPedido.Pago));
    }

    [Fact]
    public async Task Webhook_com_assinatura_invalida_e_registrado_mas_ignorado()
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();
        var pix = (await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId)).Valor!;
        c.Gateway.Aprovar(pix.GatewayId!.Value);

        await c.Pagamentos.ProcessarNotificacaoAsync(Webhook(pix.GatewayId.Value, assinaturaValida: false));

        Assert.Equal(0, c.Gateway.Consultas);
        Assert.Equal(StatusPedido.AguardandoPagamento, (await c.RecarregarPedidoAsync(pedidoId)).Status);
        var registro = await c.Db.NotificacoesGateway.SingleAsync();
        Assert.False(registro.AssinaturaValida);
    }

    [Fact]
    public async Task Cartao_aprovado_na_hora()   // RF11
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();

        var r = await c.Pagamentos.PagarComCartaoAsync(cliente.Id, pedidoId, new DadosCartao("tok_123", "visa", "25", 3, "CPF", "529.982.247-25"));

        Assert.True(r.Sucesso, r.Erro);
        Assert.True(r.Valor!.Aprovado);
        Assert.Equal(225.40m, c.Gateway.UltimoCartao!.Valor);   // o servidor usa o total do pedido
        Assert.Equal("52998224725", c.Gateway.UltimoCartao.NumeroDocumento);
        var pedido = await c.RecarregarPedidoAsync(pedidoId);
        Assert.Equal(StatusPedido.Pago, pedido.Status);
        var pagamento = pedido.Pagamentos.Single();
        Assert.Equal(3, pagamento.Parcelas);
        Assert.Equal("1234", pagamento.UltimosDigitos);
    }

    [Fact]
    public async Task Cartao_recusado_explica_o_motivo_e_pedido_continua_aguardando()   // RF11
    {
        using var c = new Cenario();
        var (cliente, produto, pedidoId) = await c.CriarPedidoAsync();
        c.Gateway.StatusCartao = "rejected";
        c.Gateway.DetalheCartao = "cc_rejected_insufficient_amount";

        var r = await c.Pagamentos.PagarComCartaoAsync(cliente.Id, pedidoId, new DadosCartao("tok", "master", null, 1, null, null));

        Assert.False(r.Sucesso);
        Assert.Contains("saldo ou limite insuficiente", r.Erro);
        Assert.Equal(StatusPedido.AguardandoPagamento, (await c.RecarregarPedidoAsync(pedidoId)).Status);
        Assert.Equal(2, (await c.RecarregarProdutoAsync(produto.Id)).EstoqueReservado);   // reserva mantida
    }

    [Fact]
    public async Task Cartao_em_analise_coloca_pedido_em_analise()
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();
        c.Gateway.StatusCartao = "in_process";
        c.Gateway.DetalheCartao = "pending_review_manual";

        var r = await c.Pagamentos.PagarComCartaoAsync(cliente.Id, pedidoId, new DadosCartao("tok", "visa", null, 1, null, null));

        Assert.True(r.Sucesso);
        Assert.True(r.Valor!.EmAnalise);
        Assert.Equal(StatusPedido.PagamentoEmAnalise, (await c.RecarregarPedidoAsync(pedidoId)).Status);
    }

    [Fact]
    public async Task Parcelas_respeitam_a_parcela_minima()   // RN06
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync(quantidade: 1, preco: 30m);   // total 55,40 → máx. 5x de R$ 10

        var r = await c.Pagamentos.PagarComCartaoAsync(cliente.Id, pedidoId, new DadosCartao("tok", "visa", null, 6, null, null));

        Assert.False(r.Sucesso);
        Assert.Contains("5x", r.Erro);
        Assert.Empty(c.Gateway.Pagamentos);
    }

    [Fact]
    public async Task Pagamento_de_outro_cliente_e_negado()   // IDOR
    {
        using var c = new Cenario();
        var (_, _, pedidoId) = await c.CriarPedidoAsync();
        var intruso = await c.CriarClienteAsync();

        var r = await c.Pagamentos.GerarPixAsync(intruso.Id, pedidoId);

        Assert.False(r.Sucesso);
        Assert.Empty(c.Gateway.Pagamentos);
    }

    [Fact]
    public async Task Falha_do_gateway_nao_quebra_o_pedido()
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();
        c.Gateway.Falhar = true;

        var r = await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId);

        Assert.False(r.Sucesso);
        var pedido = await c.RecarregarPedidoAsync(pedidoId);
        Assert.Equal(StatusPedido.AguardandoPagamento, pedido.Status);
        Assert.Equal(StatusPagamento.Cancelado, pedido.Pagamentos.Single().Status);
    }

    [Fact]
    public async Task Pedido_expirado_sem_pagamento_e_cancelado_e_libera_reserva()   // RN04
    {
        using var c = new Cenario();
        var (cliente, produto, pedidoId) = await c.CriarPedidoAsync(quantidade: 2);
        var pix = (await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId)).Valor!;
        c.Gateway.Expirar(pix.GatewayId!.Value);
        c.Relogio.Avancar(TimeSpan.FromMinutes(31));

        var cancelados = await c.Pagamentos.ExpirarPedidosAsync();

        Assert.Equal(1, cancelados);
        Assert.Equal(StatusPedido.Cancelado, (await c.RecarregarPedidoAsync(pedidoId)).Status);
        Assert.Equal(0, (await c.RecarregarProdutoAsync(produto.Id)).EstoqueReservado);
    }

    [Fact]
    public async Task Antes_de_expirar_consulta_o_gateway_e_confirma_pix_pago_sem_webhook()
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();
        var pix = (await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId)).Valor!;
        c.Gateway.Aprovar(pix.GatewayId!.Value);   // pago, mas o webhook se perdeu
        c.Relogio.Avancar(TimeSpan.FromMinutes(31));

        var cancelados = await c.Pagamentos.ExpirarPedidosAsync();

        Assert.Equal(0, cancelados);
        Assert.Equal(StatusPedido.Pago, (await c.RecarregarPedidoAsync(pedidoId)).Status);
    }

    [Fact]
    public async Task Pix_pendente_recebe_tolerancia_antes_de_cancelar()
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();
        await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId);
        c.Relogio.Avancar(TimeSpan.FromMinutes(35));   // passou dos 30, mas dentro dos 15 de tolerância

        Assert.Equal(0, await c.Pagamentos.ExpirarPedidosAsync());
        c.Relogio.Avancar(TimeSpan.FromMinutes(15));
        Assert.Equal(1, await c.Pagamentos.ExpirarPedidosAsync());
    }

    [Fact]
    public async Task Pagamento_aprovado_depois_do_cancelamento_e_estornado()
    {
        using var c = new Cenario();
        var (cliente, produto, pedidoId) = await c.CriarPedidoAsync(quantidade: 1, estoque: 5);
        var pix = (await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId)).Valor!;
        c.Gateway.Expirar(pix.GatewayId!.Value);
        c.Relogio.Avancar(TimeSpan.FromHours(1));
        await c.Pagamentos.ExpirarPedidosAsync();
        c.Db.ChangeTracker.Clear();

        c.Gateway.Aprovar(pix.GatewayId.Value);   // o banco do cliente liquidou atrasado
        await c.Pagamentos.ProcessarNotificacaoAsync(Webhook(pix.GatewayId.Value, "tardio"));

        Assert.Contains(pix.GatewayId.Value, c.Gateway.Estornos);
        var pedido = await c.RecarregarPedidoAsync(pedidoId);
        Assert.Equal(StatusPedido.Cancelado, pedido.Status);
        Assert.Equal(StatusPagamento.Estornado, pedido.Pagamentos.Single().Status);
        Assert.Equal(5, (await c.RecarregarProdutoAsync(produto.Id)).Estoque);   // estoque não foi baixado
    }

    [Fact]
    public async Task Segundo_pagamento_aprovado_do_mesmo_pedido_e_estornado()
    {
        using var c = new Cenario();
        var (cliente, produto, pedidoId) = await c.CriarPedidoAsync(quantidade: 1, estoque: 5);
        var pix = (await c.Pagamentos.GerarPixAsync(cliente.Id, pedidoId)).Valor!;
        await c.Pagamentos.PagarComCartaoAsync(cliente.Id, pedidoId, new DadosCartao("tok", "visa", null, 1, null, null));
        c.Db.ChangeTracker.Clear();

        c.Gateway.Aprovar(pix.GatewayId!.Value);   // cliente também pagou o Pix antigo
        await c.Pagamentos.ProcessarNotificacaoAsync(Webhook(pix.GatewayId.Value, "duplo"));

        Assert.Contains(pix.GatewayId.Value, c.Gateway.Estornos);
        Assert.Equal(4, (await c.RecarregarProdutoAsync(produto.Id)).Estoque);   // baixou só uma vez
    }
}
