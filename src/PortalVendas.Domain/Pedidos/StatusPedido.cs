namespace PortalVendas.Domain.Pedidos;

public enum StatusPedido : byte
{
    AguardandoPagamento = 1,
    PagamentoEmAnalise = 2,
    Pago = 3,
    EmSeparacao = 4,
    Enviado = 5,
    Entregue = 6,
    Cancelado = 9
}

public static class StatusPedidoExtensions
{
    public static string Descricao(this StatusPedido status) => status switch
    {
        StatusPedido.AguardandoPagamento => "Aguardando pagamento",
        StatusPedido.PagamentoEmAnalise => "Pagamento em análise",
        StatusPedido.Pago => "Pago",
        StatusPedido.EmSeparacao => "Em separação",
        StatusPedido.Enviado => "Enviado",
        StatusPedido.Entregue => "Entregue",
        StatusPedido.Cancelado => "Cancelado",
        _ => status.ToString()
    };

    /// <summary>Classe de cor do Bootstrap para o selo de status.</summary>
    public static string CorBootstrap(this StatusPedido status) => status switch
    {
        StatusPedido.AguardandoPagamento => "warning",
        StatusPedido.PagamentoEmAnalise => "info",
        StatusPedido.Pago => "primary",
        StatusPedido.EmSeparacao => "primary",
        StatusPedido.Enviado => "success",
        StatusPedido.Entregue => "success",
        StatusPedido.Cancelado => "secondary",
        _ => "light"
    };
}
