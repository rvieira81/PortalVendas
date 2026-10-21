namespace PortalVendas.Domain.Catalogo;

public class ProdutoImagem
{
    public int Id { get; set; }
    public int ProdutoId { get; set; }
    public string Arquivo { get; set; } = string.Empty;
    public int Ordem { get; set; }
}
