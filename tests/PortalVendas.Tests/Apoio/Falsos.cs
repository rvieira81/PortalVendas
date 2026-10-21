using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Frete;
using PortalVendas.Application.Pagamentos;

namespace PortalVendas.Tests.Apoio;

public sealed class RelogioFalso : IRelogio
{
    public RelogioFalso(DateTime agoraUtc) => AgoraUtc = agoraUtc;
    public DateTime AgoraUtc { get; set; }
    public void Avancar(TimeSpan tempo) => AgoraUtc = AgoraUtc.Add(tempo);
}

public sealed class EmailFalso : IEmailSender
{
    public List<(string Para, string Assunto, string Html)> Enviados { get; } = new();

    public Task EnviarAsync(string para, string assunto, string html, CancellationToken ct = default)
    {
        Enviados.Add((para, assunto, html));
        return Task.CompletedTask;
    }
}

public sealed class LinksFalsos : ILinksAplicacao
{
    public string ConfirmarEmail(string token) => "https://teste/confirmar?token=" + token;
    public string RedefinirSenha(string token) => "https://teste/redefinir?token=" + token;
    public string Pedido(int pedidoId) => "https://teste/pedidos/" + pedidoId;
    public string Inicio() => "https://teste/";
}

public sealed class ProvedorFreteFalso : IProvedorFrete
{
    public List<CotacaoProvedor> Cotacoes { get; } = new()
    {
        new CotacaoProvedor(1, "Correios", "PAC", 25.40m, 5),
        new CotacaoProvedor(2, "Correios", "SEDEX", 48.90m, 2)
    };
    public bool Falhar { get; set; }
    public int Chamadas { get; private set; }
    public SolicitacaoFrete? Ultima { get; private set; }

    public Task<IReadOnlyList<CotacaoProvedor>> CotarAsync(SolicitacaoFrete solicitacao, CancellationToken ct = default)
    {
        Chamadas++;
        Ultima = solicitacao;
        if (Falhar) throw new HttpRequestException("Melhor Envio fora do ar (teste)");
        return Task.FromResult<IReadOnlyList<CotacaoProvedor>>(Cotacoes.ToList());
    }
}

public sealed class GatewayFalso : IGatewayPagamento
{
    private long _proximoId = 9000;

    public Dictionary<long, RespostaGateway> Pagamentos { get; } = new();
    public List<long> Estornos { get; } = new();
    public int Consultas { get; private set; }
    public bool Falhar { get; set; }
    public string StatusCartao { get; set; } = "approved";
    public string? DetalheCartao { get; set; } = "accredited";
    public SolicitacaoCartao? UltimoCartao { get; private set; }

    public Task<RespostaGateway> CriarPixAsync(SolicitacaoPix s, CancellationToken ct = default)
    {
        if (Falhar) throw new GatewayPagamentoException("falha simulada");
        return Registrar(new RespostaGateway(++_proximoId, "pending", "pending_waiting_transfer", s.PedidoId.ToString(), s.Valor, "pix",
            PixQrCode: "00020126580014br.gov.bcb.pix-teste", PixQrCodeBase64: "iVBORw0KGgo=", Expiracao: s.Expiracao));
    }

    public Task<RespostaGateway> CriarBoletoAsync(SolicitacaoBoleto s, CancellationToken ct = default)
    {
        if (Falhar) throw new GatewayPagamentoException("falha simulada");
        return Registrar(new RespostaGateway(++_proximoId, "pending", "pending_waiting_payment", s.PedidoId.ToString(), s.Valor, "bolbradesco",
            BoletoUrl: "https://teste/boleto.pdf", BoletoLinhaDigitavel: "23793381286000000000000000000000000000000000000", Expiracao: s.Vencimento));
    }

    public Task<RespostaGateway> CriarCartaoAsync(SolicitacaoCartao s, CancellationToken ct = default)
    {
        if (Falhar) throw new GatewayPagamentoException("falha simulada");
        UltimoCartao = s;
        return Registrar(new RespostaGateway(++_proximoId, StatusCartao, DetalheCartao, s.PedidoId.ToString(), s.Valor, s.MetodoPagamentoId,
            Parcelas: s.Parcelas, UltimosDigitos: "1234"));
    }

    public Task<RespostaGateway> ConsultarAsync(long gatewayId, CancellationToken ct = default)
    {
        Consultas++;
        return Task.FromResult(Pagamentos[gatewayId]);
    }

    public Task EstornarAsync(long gatewayId, Guid chaveIdempotencia, CancellationToken ct = default)
    {
        if (Falhar) throw new GatewayPagamentoException("falha simulada");
        Estornos.Add(gatewayId);
        Pagamentos[gatewayId] = Pagamentos[gatewayId] with { Status = "refunded", StatusDetalhe = "refunded" };
        return Task.CompletedTask;
    }

    /// <summary>Simula o cliente pagando (o Mercado Pago passa a responder "approved").</summary>
    public void Aprovar(long gatewayId) =>
        Pagamentos[gatewayId] = Pagamentos[gatewayId] with { Status = "approved", StatusDetalhe = "accredited" };

    public void Expirar(long gatewayId) =>
        Pagamentos[gatewayId] = Pagamentos[gatewayId] with { Status = "cancelled", StatusDetalhe = "expired" };

    private Task<RespostaGateway> Registrar(RespostaGateway r)
    {
        Pagamentos[r.Id] = r;
        return Task.FromResult(r);
    }
}
