namespace PortalVendas.Domain.Pedidos;

/// <summary>Canal em que a venda foi feita.</summary>
public enum OrigemPedido : byte
{
    Site = 1,
    WhatsApp = 2
}

public static class OrigemPedidoExtensions
{
    public static string Descricao(this OrigemPedido o) => o switch
    {
        OrigemPedido.Site => "Site",
        OrigemPedido.WhatsApp => "WhatsApp",
        _ => o.ToString()
    };
}
