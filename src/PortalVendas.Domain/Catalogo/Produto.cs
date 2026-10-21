using PortalVendas.Domain.Comum;

namespace PortalVendas.Domain.Catalogo;

public class Produto
{
    /// <summary>RN09: limite por produto no carrinho.</summary>
    public const int QuantidadeMaximaPorItem = 10;

    public int Id { get; set; }
    public int CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public decimal? PrecoDe { get; set; }
    public int Estoque { get; set; }
    public int EstoqueReservado { get; set; }
    public decimal PesoKg { get; set; }
    public decimal AlturaCm { get; set; }
    public decimal LarguraCm { get; set; }
    public decimal ComprimentoCm { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public string CriadoPor { get; set; } = string.Empty;
    public DateTime? AlteradoEm { get; set; }
    public string? AlteradoPor { get; set; }

    public List<ProdutoImagem> Imagens { get; set; } = new();

    /// <summary>Estoque que pode ser vendido agora (físico menos o reservado em pedidos não pagos).</summary>
    public int Disponivel => Math.Max(0, Estoque - EstoqueReservado);

    /// <summary>Quanto o cliente pode colocar no carrinho (RN09).</summary>
    public int QuantidadeMaximaCompra => Math.Min(Disponivel, QuantidadeMaximaPorItem);

    public IReadOnlyList<string> Validar()
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(Nome)) erros.Add("Informe o nome do produto.");
        if (Nome.Length > 200) erros.Add("O nome pode ter no máximo 200 caracteres.");
        if (string.IsNullOrWhiteSpace(Sku)) erros.Add("Informe o SKU.");
        if (Sku.Length > 50) erros.Add("O SKU pode ter no máximo 50 caracteres.");
        if (Descricao.Length > 4000) erros.Add("A descrição pode ter no máximo 4.000 caracteres.");
        if (Preco <= 0) erros.Add("O preço deve ser maior que zero.");
        if (PrecoDe is not null && PrecoDe <= Preco) erros.Add("O preço \"de\" deve ser maior que o preço de venda.");
        if (Estoque < 0) erros.Add("O estoque não pode ser negativo.");
        if (Estoque < EstoqueReservado)
            erros.Add($"Há {EstoqueReservado} unidade(s) reservada(s) em pedidos aguardando pagamento; o estoque não pode ser menor que isso.");
        if (PesoKg <= 0) erros.Add("Informe o peso (kg) para o cálculo do frete.");
        if (AlturaCm <= 0 || LarguraCm <= 0 || ComprimentoCm <= 0)
            erros.Add("Informe altura, largura e comprimento (cm) para o cálculo do frete.");
        if (CategoriaId <= 0) erros.Add("Escolha a categoria.");
        return erros;
    }

    public void NormalizarCampos()
    {
        Nome = Texto.Limpar(Nome) ?? string.Empty;
        Sku = (Texto.Limpar(Sku) ?? string.Empty).ToUpperInvariant();
        Descricao = Descricao?.Trim() ?? string.Empty;
    }
}
