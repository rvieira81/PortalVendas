namespace PortalVendas.Domain.Pedidos;

public class PedidoHistorico
{
    private PedidoHistorico() { }   // EF Core

    internal PedidoHistorico(StatusPedido? anterior, StatusPedido novo, string? observacao, string autor, DateTime criadoEm)
    {
        StatusAnterior = anterior;
        StatusNovo = novo;
        Observacao = observacao;
        Autor = autor.Length > 100 ? autor[..100] : autor;
        CriadoEm = criadoEm;
    }

    public long Id { get; private set; }
    public int PedidoId { get; private set; }
    public StatusPedido? StatusAnterior { get; private set; }
    public StatusPedido StatusNovo { get; private set; }
    public string? Observacao { get; private set; }
    public string Autor { get; private set; } = string.Empty;
    public DateTime CriadoEm { get; private set; }
}
