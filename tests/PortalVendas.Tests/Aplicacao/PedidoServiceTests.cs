using PortalVendas.Application.Pagamentos;
using PortalVendas.Domain.Pagamentos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Tests.Apoio;

namespace PortalVendas.Tests.Aplicacao;

public class PedidoServiceTests
{
    [Fact]
    public async Task Cliente_nao_ve_pedido_de_outro_cliente()   // RF14, IDOR
    {
        using var c = new Cenario();
        var (dono, _, pedidoId) = await c.CriarPedidoAsync();
        var outro = await c.CriarClienteAsync();

        Assert.NotNull(await c.Pedidos.ObterDoClienteAsync(dono.Id, pedidoId));
        Assert.Null(await c.Pedidos.ObterDoClienteAsync(outro.Id, pedidoId));
        Assert.Null(await c.Pedidos.ObterStatusDoClienteAsync(outro.Id, pedidoId));
        Assert.False((await c.Pedidos.CancelarPeloClienteAsync(outro.Id, pedidoId)).Sucesso);
    }

    [Fact]
    public async Task Cancelar_antes_de_pagar_libera_a_reserva()   // RF16
    {
        using var c = new Cenario();
        var (cliente, produto, pedidoId) = await c.CriarPedidoAsync(quantidade: 3);

        var r = await c.Pedidos.CancelarPeloClienteAsync(cliente.Id, pedidoId);

        Assert.True(r.Sucesso, r.Erro);
        Assert.Equal(0, (await c.RecarregarProdutoAsync(produto.Id)).EstoqueReservado);
        Assert.Empty(c.Gateway.Estornos);
    }

    [Fact]
    public async Task Cancelar_pedido_pago_estorna_e_devolve_estoque()   // RF16
    {
        using var c = new Cenario();
        var (cliente, produto, pedidoId) = await c.CriarPedidoAsync(quantidade: 2, estoque: 10);
        await c.Pagamentos.PagarComCartaoAsync(cliente.Id, pedidoId, new DadosCartao("tok", "visa", null, 1, null, null));
        Assert.Equal(8, (await c.RecarregarProdutoAsync(produto.Id)).Estoque);
        c.Db.ChangeTracker.Clear();

        var r = await c.Pedidos.CancelarPeloClienteAsync(cliente.Id, pedidoId);

        Assert.True(r.Sucesso, r.Erro);
        Assert.Single(c.Gateway.Estornos);
        Assert.Equal(10, (await c.RecarregarProdutoAsync(produto.Id)).Estoque);
        var pedido = await c.RecarregarPedidoAsync(pedidoId);
        Assert.Equal(StatusPedido.Cancelado, pedido.Status);
        Assert.Equal(StatusPagamento.Estornado, pedido.Pagamentos.Single().Status);
    }

    [Fact]
    public async Task Falha_no_estorno_mantem_o_pedido_como_estava()
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();
        await c.Pagamentos.PagarComCartaoAsync(cliente.Id, pedidoId, new DadosCartao("tok", "visa", null, 1, null, null));
        c.Db.ChangeTracker.Clear();
        c.Gateway.Falhar = true;

        var r = await c.Pedidos.CancelarPeloClienteAsync(cliente.Id, pedidoId);

        Assert.False(r.Sucesso);
        Assert.Equal(StatusPedido.Pago, (await c.RecarregarPedidoAsync(pedidoId)).Status);
    }

    [Fact]
    public async Task Admin_avanca_status_ate_entregue_com_rastreio()   // RF18
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();

        // Sem pagamento não dá para separar
        Assert.False((await c.Pedidos.AvancarStatusAsync(pedidoId, StatusPedido.EmSeparacao, null, "admin@loja")).Sucesso);

        await c.Pagamentos.PagarComCartaoAsync(cliente.Id, pedidoId, new DadosCartao("tok", "visa", null, 1, null, null));
        c.Db.ChangeTracker.Clear();

        Assert.True((await c.Pedidos.AvancarStatusAsync(pedidoId, StatusPedido.EmSeparacao, null, "admin@loja")).Sucesso);
        Assert.False((await c.Pedidos.AvancarStatusAsync(pedidoId, StatusPedido.Enviado, "", "admin@loja")).Sucesso);   // sem rastreio
        Assert.True((await c.Pedidos.AvancarStatusAsync(pedidoId, StatusPedido.Enviado, "AA123456789BR", "admin@loja")).Sucesso);
        Assert.True((await c.Pedidos.AvancarStatusAsync(pedidoId, StatusPedido.Entregue, null, "admin@loja")).Sucesso);

        var pedido = await c.RecarregarPedidoAsync(pedidoId);
        Assert.Equal(StatusPedido.Entregue, pedido.Status);
        Assert.Equal("AA123456789BR", pedido.CodigoRastreio);
        Assert.Contains(pedido.Historico, h => h.Autor == "admin@loja" && h.StatusNovo == StatusPedido.Enviado);   // RNF08
        Assert.Contains(c.Email.Enviados, e => e.Assunto.Contains("Pedido enviado"));
    }
}
