namespace PortalVendas.Domain.Pedidos;

public class PedidoItem
{
    private PedidoItem() { }   // EF Core

    internal PedidoItem(int produtoId, string nomeProduto, string sku, decimal precoUnitario, int quantidade)
    {
        ProdutoId = produtoId;
        NomeProduto = nomeProduto;
        Sku = sku;
        PrecoUnitario = precoUnitario;
        Quantidade = quantidade;
        Total = precoUnitario * quantidade;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public int ProdutoId { get; private set; }
    public string NomeProduto { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public decimal PrecoUnitario { get; private set; }
    public int Quantidade { get; private set; }
    public decimal Total { get; private set; }
}
