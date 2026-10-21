namespace PortalVendas.Domain.Catalogo;

public class Categoria
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool Ativa { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime? AlteradoEm { get; set; }

    public List<Produto> Produtos { get; set; } = new();
}
