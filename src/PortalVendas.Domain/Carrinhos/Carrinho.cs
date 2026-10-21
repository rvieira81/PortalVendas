using PortalVendas.Domain.Catalogo;
using PortalVendas.Domain.Comum;

namespace PortalVendas.Domain.Carrinhos;

public class Carrinho
{
    public int Id { get; set; }
    public Guid Token { get; set; } = Guid.NewGuid();
    public int? UsuarioId { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }

    public List<CarrinhoItem> Itens { get; set; } = new();

    public bool EstaVazio => Itens.Count == 0;

    /// <summary>
    /// Adiciona (ou soma) a quantidade de um produto, respeitando o disponível e o limite por item (RN09).
    /// Retorna a quantidade efetivamente no carrinho e se foi preciso limitar.
    /// </summary>
    public (int Quantidade, bool Limitado) Adicionar(Produto produto, int quantidade, DateTime agoraUtc)
    {
        if (quantidade <= 0) throw new DomainException("A quantidade deve ser maior que zero.");

        var item = Itens.FirstOrDefault(i => i.ProdutoId == produto.Id);
        var desejada = (item?.Quantidade ?? 0) + quantidade;
        var maximo = produto.QuantidadeMaximaCompra;
        var final = Math.Min(desejada, maximo);

        if (final <= 0) return (0, true);

        if (item is null)
        {
            Itens.Add(new CarrinhoItem { ProdutoId = produto.Id, Quantidade = final, PrecoNaInclusao = produto.Preco });
        }
        else
        {
            item.Quantidade = final;
        }
        AtualizadoEm = agoraUtc;
        return (final, final < desejada);
    }

    public (int Quantidade, bool Limitado) DefinirQuantidade(Produto produto, int quantidade, DateTime agoraUtc)
    {
        var item = Itens.FirstOrDefault(i => i.ProdutoId == produto.Id);
        if (item is null) return (0, false);

        if (quantidade <= 0)
        {
            Itens.Remove(item);
            AtualizadoEm = agoraUtc;
            return (0, false);
        }

        var final = Math.Min(quantidade, produto.QuantidadeMaximaCompra);
        if (final <= 0)
        {
            Itens.Remove(item);
        }
        else
        {
            item.Quantidade = final;
        }
        AtualizadoEm = agoraUtc;
        return (final, final < quantidade);
    }

    public bool Remover(int produtoId, DateTime agoraUtc)
    {
        var removidos = Itens.RemoveAll(i => i.ProdutoId == produtoId);
        if (removidos > 0) AtualizadoEm = agoraUtc;
        return removidos > 0;
    }

    /// <summary>RF04: ao logar, os itens do carrinho anônimo entram no carrinho da conta.</summary>
    public void Mesclar(Carrinho anonimo, IReadOnlyDictionary<int, Produto> produtos, DateTime agoraUtc)
    {
        foreach (var itemAnonimo in anonimo.Itens)
        {
            if (!produtos.TryGetValue(itemAnonimo.ProdutoId, out var produto) || !produto.Ativo) continue;

            var existente = Itens.FirstOrDefault(i => i.ProdutoId == itemAnonimo.ProdutoId);
            // Não soma: fica a maior quantidade escolhida, para não duplicar sem querer
            var desejada = Math.Max(existente?.Quantidade ?? 0, itemAnonimo.Quantidade);
            var final = Math.Min(desejada, produto.QuantidadeMaximaCompra);
            if (final <= 0) continue;

            if (existente is null)
                Itens.Add(new CarrinhoItem { ProdutoId = produto.Id, Quantidade = final, PrecoNaInclusao = itemAnonimo.PrecoNaInclusao });
            else
                existente.Quantidade = final;
        }
        AtualizadoEm = agoraUtc;
    }
}

public class CarrinhoItem
{
    public int Id { get; set; }
    public int CarrinhoId { get; set; }
    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }
    public int Quantidade { get; set; }
    public decimal PrecoNaInclusao { get; set; }
}
