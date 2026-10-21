namespace PortalVendas.Domain.Pagamentos;

public enum MetodoPagamento : byte
{
    Pix = 1,
    CartaoCredito = 2,
    Boleto = 3
}

public enum StatusPagamento : byte
{
    Pendente = 1,
    EmAnalise = 2,
    Aprovado = 3,
    Recusado = 4,
    Cancelado = 5,
    Estornado = 6,
    Contestado = 7
}

public static class PagamentoExtensions
{
    public static string Descricao(this MetodoPagamento m) => m switch
    {
        MetodoPagamento.Pix => "Pix",
        MetodoPagamento.CartaoCredito => "Cartão de crédito",
        MetodoPagamento.Boleto => "Boleto",
        _ => m.ToString()
    };

    public static string Descricao(this StatusPagamento s) => s switch
    {
        StatusPagamento.Pendente => "Pendente",
        StatusPagamento.EmAnalise => "Em análise",
        StatusPagamento.Aprovado => "Aprovado",
        StatusPagamento.Recusado => "Recusado",
        StatusPagamento.Cancelado => "Cancelado/expirado",
        StatusPagamento.Estornado => "Estornado",
        StatusPagamento.Contestado => "Contestado",
        _ => s.ToString()
    };

    /// <summary>Status que não mudam mais (salvo estorno/contestação de um aprovado).</summary>
    public static bool EhFinalSemAprovacao(this StatusPagamento s) =>
        s is StatusPagamento.Recusado or StatusPagamento.Cancelado or StatusPagamento.Estornado;
}

public class Pagamento
{
    private Pagamento() { }   // EF Core

    public Pagamento(int pedidoId, MetodoPagamento metodo, decimal valor, DateTime agoraUtc)
    {
        PedidoId = pedidoId;
        Metodo = metodo;
        Valor = valor;
        Status = StatusPagamento.Pendente;
        ChaveIdempotencia = Guid.NewGuid();
        CriadoEm = agoraUtc;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public MetodoPagamento Metodo { get; private set; }
    public StatusPagamento Status { get; private set; }
    public string? StatusGateway { get; private set; }
    public string? StatusDetalhe { get; private set; }
    public long? GatewayId { get; private set; }
    public Guid ChaveIdempotencia { get; private set; }
    public decimal Valor { get; private set; }
    public int? Parcelas { get; private set; }
    public string? Bandeira { get; private set; }
    public string? UltimosDigitos { get; private set; }
    public string? PixQrCode { get; private set; }
    public string? PixQrCodeBase64 { get; private set; }
    public string? BoletoUrl { get; private set; }
    public string? BoletoLinhaDigitavel { get; private set; }
    public DateTime? ExpiraEm { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime? AlteradoEm { get; private set; }

    public bool PixOuBoletoAindaValido(DateTime agoraUtc) =>
        Status == StatusPagamento.Pendente && GatewayId is not null && ExpiraEm is not null && ExpiraEm > agoraUtc;

    /// <summary>Converte o status do Mercado Pago para o nosso.</summary>
    public static StatusPagamento ConverterStatus(string? statusGateway) => statusGateway?.ToLowerInvariant() switch
    {
        "approved" => StatusPagamento.Aprovado,
        "authorized" or "in_process" => StatusPagamento.EmAnalise,
        "pending" => StatusPagamento.Pendente,
        "rejected" => StatusPagamento.Recusado,
        "cancelled" or "expired" => StatusPagamento.Cancelado,
        "refunded" => StatusPagamento.Estornado,
        "charged_back" or "in_mediation" => StatusPagamento.Contestado,
        _ => StatusPagamento.Pendente
    };

    /// <summary>Atualiza com a resposta do gateway. Retorna true se o status mudou.</summary>
    public bool AtualizarDoGateway(long gatewayId, string? statusGateway, string? statusDetalhe, DateTime agoraUtc)
    {
        if (GatewayId is not null && GatewayId != gatewayId)
            throw new Comum.DomainException("Resposta do gateway para outro pagamento.");

        var anterior = Status;
        GatewayId = gatewayId;
        StatusGateway = Truncar(statusGateway, 30);
        StatusDetalhe = Truncar(statusDetalhe, 100);
        Status = ConverterStatus(statusGateway);
        AlteradoEm = agoraUtc;
        return anterior != Status;
    }

    public void DefinirDadosPix(string? qrCode, string? qrCodeBase64, DateTime? expiraEmUtc)
    {
        PixQrCode = Truncar(qrCode, 1000);
        PixQrCodeBase64 = qrCodeBase64;
        ExpiraEm = expiraEmUtc;
    }

    public void DefinirDadosBoleto(string? url, string? linhaDigitavel, DateTime? expiraEmUtc)
    {
        BoletoUrl = Truncar(url, 500);
        BoletoLinhaDigitavel = Truncar(linhaDigitavel, 60);
        ExpiraEm = expiraEmUtc;
    }

    public void DefinirDadosCartao(int parcelas, string? bandeira, string? ultimosDigitos)
    {
        Parcelas = parcelas;
        Bandeira = Truncar(bandeira, 30);
        UltimosDigitos = ultimosDigitos is { Length: 4 } ? ultimosDigitos : null;
    }

    /// <summary>Falha de comunicação antes de o gateway registrar o pagamento.</summary>
    public void MarcarFalhaComunicacao(string detalhe, DateTime agoraUtc)
    {
        Status = StatusPagamento.Cancelado;
        StatusDetalhe = Truncar(detalhe, 100);
        AlteradoEm = agoraUtc;
    }

    public void MarcarEstornado(DateTime agoraUtc)
    {
        Status = StatusPagamento.Estornado;
        StatusGateway = "refunded";
        AlteradoEm = agoraUtc;
    }

    private static string? Truncar(string? v, int max) => v is null ? null : (v.Length > max ? v[..max] : v);
}
