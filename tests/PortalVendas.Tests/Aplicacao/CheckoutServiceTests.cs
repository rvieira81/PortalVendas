using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Carrinhos;
using PortalVendas.Application.Pedidos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Tests.Apoio;

namespace PortalVendas.Tests.Aplicacao;

public class CheckoutServiceTests
{
    [Fact]
    public async Task Cria_pedido_reserva_estoque_e_esvazia_carrinho()   // RF09, RN02
    {
        using var c = new Cenario();
        var (cliente, produto, pedidoId) = await c.CriarPedidoAsync(quantidade: 2, estoque: 10, preco: 100m);

        var pedido = await c.RecarregarPedidoAsync(pedidoId);
        Assert.Equal(StatusPedido.AguardandoPagamento, pedido.Status);
        Assert.Equal(200m, pedido.Subtotal);
        Assert.Equal(25.40m, pedido.ValorFrete);   // recotado no servidor (opção ME-1)
        Assert.Equal(225.40m, pedido.Total);
        Assert.Equal("Rua das Flores", pedido.Entrega.Logradouro);

        var p = await c.RecarregarProdutoAsync(produto.Id);
        Assert.Equal(10, p.Estoque);
        Assert.Equal(2, p.EstoqueReservado);

        Assert.Equal(0, await c.Carrinhos.ContarItensAsync(new IdentificacaoCarrinho(null, cliente.Id)));
        Assert.Contains(c.Email.Enviados, e => e.Assunto.Contains("Pedido recebido"));
    }

    [Fact]
    public async Task Duplo_clique_cria_um_unico_pedido()   // RF09 idempotência
    {
        using var c = new Cenario();
        var cliente = await c.CriarClienteAsync();
        var endereco = await c.CriarEnderecoAsync(cliente.Id);
        var produto = await c.CriarProdutoAsync(estoque: 10);
        await c.Carrinhos.AdicionarAsync(new IdentificacaoCarrinho(null, cliente.Id), produto.Id, 1);
        var chave = Guid.NewGuid();

        var r1 = await c.Checkout.CriarPedidoAsync(new ComandoCriarPedido(cliente.Id, chave, endereco.Id, "ME-1"));
        var r2 = await c.Checkout.CriarPedidoAsync(new ComandoCriarPedido(cliente.Id, chave, endereco.Id, "ME-1"));

        Assert.True(r1.Sucesso);
        Assert.True(r2.Sucesso);
        Assert.Equal(r1.Valor, r2.Valor);
        Assert.Equal(1, await c.Db.Pedidos.CountAsync());
        Assert.Equal(1, (await c.RecarregarProdutoAsync(produto.Id)).EstoqueReservado);
    }

    [Fact]
    public async Task Ultima_unidade_vendida_para_outro_cliente_no_meio_do_checkout()   // RF09, RN03
    {
        using var c = new Cenario();
        var cliente = await c.CriarClienteAsync();
        var endereco = await c.CriarEnderecoAsync(cliente.Id);
        var produto = await c.CriarProdutoAsync(estoque: 1);
        await c.Carrinhos.AdicionarAsync(new IdentificacaoCarrinho(null, cliente.Id), produto.Id, 1);

        // Outro cliente reserva a última unidade direto no banco, depois que este montou o carrinho
        await using (var outro = c.NovoContexto())
            await outro.Database.ExecuteSqlRawAsync("UPDATE Produto SET EstoqueReservado = 1 WHERE Id = {0}", produto.Id);
        c.Db.ChangeTracker.Clear();

        var r = await c.Checkout.CriarPedidoAsync(new ComandoCriarPedido(cliente.Id, Guid.NewGuid(), endereco.Id, "ME-1"));

        Assert.False(r.Sucesso);
        Assert.Contains("estoque", r.Erro!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await c.Db.Pedidos.CountAsync());
        Assert.Equal(1, (await c.RecarregarProdutoAsync(produto.Id)).EstoqueReservado);
    }

    [Fact]
    public async Task Reserva_atomica_nunca_passa_do_estoque()
    {
        using var c = new Cenario();
        var produto = await c.CriarProdutoAsync(estoque: 3);

        Assert.True(await c.Estoque.ReservarAsync(produto.Id, 2));
        Assert.False(await c.Estoque.ReservarAsync(produto.Id, 2));   // só sobrou 1
        Assert.True(await c.Estoque.ReservarAsync(produto.Id, 1));
        Assert.Equal(3, (await c.RecarregarProdutoAsync(produto.Id)).EstoqueReservado);
    }

    [Fact]
    public async Task Exige_email_confirmado()   // RF05
    {
        using var c = new Cenario();
        var cliente = await c.CriarClienteAsync(emailConfirmado: false);
        var endereco = await c.CriarEnderecoAsync(cliente.Id);
        var produto = await c.CriarProdutoAsync();
        await c.Carrinhos.AdicionarAsync(new IdentificacaoCarrinho(null, cliente.Id), produto.Id, 1);

        var r = await c.Checkout.CriarPedidoAsync(new ComandoCriarPedido(cliente.Id, Guid.NewGuid(), endereco.Id, "ME-1"));

        Assert.False(r.Sucesso);
        Assert.Contains("Confirme seu e-mail", r.Erro);
    }

    [Fact]
    public async Task Recusa_opcao_de_frete_inexistente()   // o valor do frete nunca vem do navegador
    {
        using var c = new Cenario();
        var cliente = await c.CriarClienteAsync();
        var endereco = await c.CriarEnderecoAsync(cliente.Id);
        var produto = await c.CriarProdutoAsync();
        await c.Carrinhos.AdicionarAsync(new IdentificacaoCarrinho(null, cliente.Id), produto.Id, 1);

        var r = await c.Checkout.CriarPedidoAsync(new ComandoCriarPedido(cliente.Id, Guid.NewGuid(), endereco.Id, "ME-999"));

        Assert.False(r.Sucesso);
        Assert.Equal(0, await c.Db.Pedidos.CountAsync());
    }

    [Fact]
    public async Task Nao_usa_endereco_de_outro_cliente()   // IDOR
    {
        using var c = new Cenario();
        var cliente = await c.CriarClienteAsync();
        var outro = await c.CriarClienteAsync();
        var enderecoDoOutro = await c.CriarEnderecoAsync(outro.Id);
        var produto = await c.CriarProdutoAsync();
        await c.Carrinhos.AdicionarAsync(new IdentificacaoCarrinho(null, cliente.Id), produto.Id, 1);

        var r = await c.Checkout.CriarPedidoAsync(new ComandoCriarPedido(cliente.Id, Guid.NewGuid(), enderecoDoOutro.Id, "ME-1"));

        Assert.False(r.Sucesso);
    }

    [Fact]
    public async Task Carrinho_anonimo_e_mesclado_no_login()   // RF04
    {
        using var c = new Cenario();
        var cliente = await c.CriarClienteAsync();
        var produto = await c.CriarProdutoAsync();
        var anonimo = await c.Carrinhos.AdicionarAsync(new IdentificacaoCarrinho(null, null), produto.Id, 2);

        await c.Carrinhos.MesclarAoEntrarAsync(anonimo.Valor, cliente.Id);

        Assert.Equal(2, await c.Carrinhos.ContarItensAsync(new IdentificacaoCarrinho(null, cliente.Id)));
    }
}
