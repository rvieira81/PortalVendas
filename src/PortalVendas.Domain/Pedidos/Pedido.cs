using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Pagamentos;

namespace PortalVendas.Domain.Pedidos;

public class Pedido
{
    /// <summary>Tempo para o cliente escolher a forma de pagamento depois de criar o pedido.</summary>
    public static readonly TimeSpan PrazoParaIniciarPagamento = TimeSpan.FromHours(2);

    private readonly List<PedidoItem> _itens = new();
    private readonly List<PedidoHistorico> _historico = new();
    private readonly List<Pagamento> _pagamentos = new();

    private Pedido() { }   // EF Core

    public int Id { get; private set; }
    public int UsuarioId { get; private set; }
    public StatusPedido Status { get; private set; }
    public OrigemPedido Origem { get; private set; } = OrigemPedido.Site;
    public Guid ChaveIdempotencia { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal ValorFrete { get; private set; }
    public decimal Desconto { get; private set; }
    public decimal Total { get; private set; }
    public int? FreteServicoId { get; private set; }
    public string FreteDescricao { get; private set; } = string.Empty;
    public int FretePrazoDias { get; private set; }
    public EnderecoEntrega Entrega { get; private set; } = null!;
    public string? CodigoRastreio { get; private set; }
    public DateTime? ExpiraEm { get; private set; }
    public DateTime? PagoEm { get; private set; }
    public DateTime? EnviadoEm { get; private set; }
    public DateTime? EntregueEm { get; private set; }
    public DateTime? CanceladoEm { get; private set; }
    public string? MotivoCancelamento { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime? AlteradoEm { get; private set; }
    public int Versao { get; private set; }

    public IReadOnlyCollection<PedidoItem> Itens => _itens;
    public IReadOnlyCollection<PedidoHistorico> Historico => _historico;
    public IReadOnlyCollection<Pagamento> Pagamentos => _pagamentos;

    public string Numero => Id.ToString("D6");

    public static Pedido Criar(
        int usuarioId,
        Guid chaveIdempotencia,
        IEnumerable<(int ProdutoId, string Nome, string Sku, decimal PrecoUnitario, int Quantidade)> itens,
        EnderecoEntrega entrega,
        int? freteServicoId,
        string freteDescricao,
        decimal valorFrete,
        int fretePrazoDias,
        decimal desconto,
        DateTime agoraUtc,
        string autor,
        OrigemPedido origem = OrigemPedido.Site)
    {
        var pedido = new Pedido
        {
            Origem = origem,
            UsuarioId = usuarioId,
            ChaveIdempotencia = chaveIdempotencia,
            Status = StatusPedido.AguardandoPagamento,
            Entrega = entrega,
            FreteServicoId = freteServicoId,
            FreteDescricao = freteDescricao,
            ValorFrete = Math.Round(valorFrete, 2),
            FretePrazoDias = fretePrazoDias,
            Desconto = Math.Round(desconto, 2),
            CriadoEm = agoraUtc,
            ExpiraEm = agoraUtc.Add(PrazoParaIniciarPagamento)
        };

        foreach (var i in itens)
        {
            if (i.Quantidade <= 0) throw new DomainException("Item de pedido com quantidade inválida.");
            if (i.PrecoUnitario <= 0) throw new DomainException("Item de pedido com preço inválido.");
            pedido._itens.Add(new PedidoItem(i.ProdutoId, i.Nome, i.Sku, i.PrecoUnitario, i.Quantidade));
        }

        if (pedido._itens.Count == 0) throw new DomainException("Pedido sem itens.");
        if (pedido.ValorFrete < 0) throw new DomainException("Frete negativo.");
        if (pedido.Desconto < 0) throw new DomainException("Desconto negativo.");

        // RN05: total calculado aqui, nunca recebido de fora
        pedido.Subtotal = pedido._itens.Sum(i => i.Total);
        if (pedido.Desconto > pedido.Subtotal + pedido.ValorFrete)
            pedido.Desconto = pedido.Subtotal + pedido.ValorFrete;
        pedido.Total = pedido.Subtotal + pedido.ValorFrete - pedido.Desconto;

        pedido._historico.Add(new PedidoHistorico(null, StatusPedido.AguardandoPagamento, "Pedido criado", autor, agoraUtc));
        return pedido;
    }

    // ---------------------------------------------------------------- transições (RN07)

    /// <summary>Transições permitidas. Qualquer outra é rejeitada.</summary>
    public static bool TransicaoPermitida(StatusPedido de, StatusPedido para) => (de, para) switch
    {
        (StatusPedido.AguardandoPagamento, StatusPedido.PagamentoEmAnalise) => true,
        (StatusPedido.AguardandoPagamento, StatusPedido.Pago) => true,
        (StatusPedido.PagamentoEmAnalise, StatusPedido.Pago) => true,
        (StatusPedido.PagamentoEmAnalise, StatusPedido.AguardandoPagamento) => true,   // análise reprovada
        (StatusPedido.Pago, StatusPedido.EmSeparacao) => true,
        (StatusPedido.EmSeparacao, StatusPedido.Enviado) => true,
        (StatusPedido.Enviado, StatusPedido.Entregue) => true,
        (StatusPedido.AguardandoPagamento, StatusPedido.Cancelado) => true,
        (StatusPedido.PagamentoEmAnalise, StatusPedido.Cancelado) => true,
        (StatusPedido.Pago, StatusPedido.Cancelado) => true,
        (StatusPedido.EmSeparacao, StatusPedido.Cancelado) => true,
        _ => false
    };

    /// <summary>Status para os quais o administrador pode mover o pedido manualmente.</summary>
    public IReadOnlyList<StatusPedido> ProximosStatusManuais() => Status switch
    {
        StatusPedido.Pago => new[] { StatusPedido.EmSeparacao },
        StatusPedido.EmSeparacao => new[] { StatusPedido.Enviado },
        StatusPedido.Enviado => new[] { StatusPedido.Entregue },
        _ => Array.Empty<StatusPedido>()
    };

    /// <summary>Estoque já foi baixado (pagamento confirmado) e a mercadoria ainda está na loja.</summary>
    public bool EstoqueBaixado => Status is StatusPedido.Pago or StatusPedido.EmSeparacao
        or StatusPedido.Enviado or StatusPedido.Entregue;

    public bool PodeReceberPagamento => Status == StatusPedido.AguardandoPagamento;

    /// <summary>RF16: o cliente cancela enquanto o pedido não foi enviado e não está em análise.</summary>
    public bool PodeSerCanceladoPeloCliente =>
        Status is StatusPedido.AguardandoPagamento or StatusPedido.Pago or StatusPedido.EmSeparacao;

    public void DefinirExpiracao(DateTime expiraEmUtc, DateTime agoraUtc)
    {
        if (Status != StatusPedido.AguardandoPagamento) return;
        ExpiraEm = expiraEmUtc;
        Tocar(agoraUtc);
    }

    public void ColocarEmAnalise(DateTime agoraUtc, string autor) =>
        Mudar(StatusPedido.PagamentoEmAnalise, agoraUtc, autor, "Pagamento em análise pela operadora");

    public void VoltarParaAguardandoPagamento(DateTime agoraUtc, string autor, string motivo) =>
        Mudar(StatusPedido.AguardandoPagamento, agoraUtc, autor, motivo);

    public void ConfirmarPagamento(DateTime agoraUtc, string autor, string observacao)
    {
        Mudar(StatusPedido.Pago, agoraUtc, autor, observacao);
        PagoEm = agoraUtc;
        ExpiraEm = null;
    }

    public void IniciarSeparacao(DateTime agoraUtc, string autor) =>
        Mudar(StatusPedido.EmSeparacao, agoraUtc, autor, null);

    public void MarcarEnviado(string codigoRastreio, DateTime agoraUtc, string autor)
    {
        var codigo = codigoRastreio?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(codigo)) throw new DomainException("Informe o código de rastreio.");
        if (codigo.Length > 50) throw new DomainException("Código de rastreio muito longo.");
        Mudar(StatusPedido.Enviado, agoraUtc, autor, $"Código de rastreio: {codigo}");
        CodigoRastreio = codigo;
        EnviadoEm = agoraUtc;
    }

    public void MarcarEntregue(DateTime agoraUtc, string autor)
    {
        Mudar(StatusPedido.Entregue, agoraUtc, autor, null);
        EntregueEm = agoraUtc;
    }

    public void Cancelar(string motivo, DateTime agoraUtc, string autor)
    {
        var texto = string.IsNullOrWhiteSpace(motivo) ? "Cancelado" : motivo.Trim();
        if (texto.Length > 300) texto = texto[..300];
        Mudar(StatusPedido.Cancelado, agoraUtc, autor, texto);
        MotivoCancelamento = texto;
        CanceladoEm = agoraUtc;
        ExpiraEm = null;
    }

    /// <summary>Registra um fato no histórico sem mudar o status (ex.: estorno automático).</summary>
    public void RegistrarObservacao(string observacao, DateTime agoraUtc, string autor)
    {
        _historico.Add(new PedidoHistorico(Status, Status, Limitar(observacao), autor, agoraUtc));
        Tocar(agoraUtc);
    }

    private void Mudar(StatusPedido novo, DateTime agoraUtc, string autor, string? observacao)
    {
        if (!TransicaoPermitida(Status, novo))
            throw new DomainException($"Não é possível mudar o pedido de \"{Status.Descricao()}\" para \"{novo.Descricao()}\".");

        _historico.Add(new PedidoHistorico(Status, novo, observacao is null ? null : Limitar(observacao), autor, agoraUtc));
        Status = novo;
        Tocar(agoraUtc);
    }

    private void Tocar(DateTime agoraUtc)
    {
        AlteradoEm = agoraUtc;
        Versao++;
    }

    private static string Limitar(string texto) => texto.Length > 300 ? texto[..300] : texto;
}
