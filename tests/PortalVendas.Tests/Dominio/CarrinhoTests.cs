using PortalVendas.Domain.Carrinhos;
using PortalVendas.Domain.Catalogo;

namespace PortalVendas.Tests.Dominio;

public class CarrinhoTests
{
    private static readonly DateTime Agora = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Produto Produto(int id, int estoque, int reservado = 0) =>
        new() { Id = id, Nome = $"P{id}", Preco = 10m, Estoque = estoque, EstoqueReservado = reservado, Ativo = true };

    [Fact]
    public void Limita_ao_estoque_disponivel()   // RF04
    {
        var c = new Carrinho();
        var (qtd, limitado) = c.Adicionar(Produto(1, estoque: 5), 6, Agora);
        Assert.Equal(5, qtd);
        Assert.True(limitado);
    }

    [Fact]
    public void Desconta_o_que_esta_reservado()
    {
        var c = new Carrinho();
        var (qtd, _) = c.Adicionar(Produto(1, estoque: 5, reservado: 4), 3, Agora);
        Assert.Equal(1, qtd);
    }

    [Fact]
    public void Limita_a_dez_unidades_por_produto()   // RN09
    {
        var c = new Carrinho();
        c.Adicionar(Produto(1, estoque: 100), 8, Agora);
        var (qtd, limitado) = c.Adicionar(Produto(1, estoque: 100), 8, Agora);
        Assert.Equal(10, qtd);
        Assert.True(limitado);
        Assert.Single(c.Itens);
    }

    [Fact]
    public void Mesclar_mantem_a_maior_quantidade_sem_duplicar()
    {
        var p1 = Produto(1, 50);
        var p2 = Produto(2, 50);
        var conta = new Carrinho();
        conta.Adicionar(p1, 2, Agora);
        var anonimo = new Carrinho();
        anonimo.Adicionar(p1, 3, Agora);
        anonimo.Adicionar(p2, 1, Agora);

        conta.Mesclar(anonimo, new Dictionary<int, Produto> { [1] = p1, [2] = p2 }, Agora);

        Assert.Equal(2, conta.Itens.Count);
        Assert.Equal(3, conta.Itens.First(i => i.ProdutoId == 1).Quantidade);
    }
}
