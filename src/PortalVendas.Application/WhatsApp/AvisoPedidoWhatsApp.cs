using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Admin;
using PortalVendas.Domain.Pedidos;

namespace PortalVendas.Application.WhatsApp;

/// <summary>Avisa no WhatsApp as mudanças de status dos pedidos de clientes que têm conversa com a loja.</summary>
public sealed class AvisoPedidoWhatsApp : IAvisoPedidoWhatsApp
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly IAppDbContext _db;
    private readonly ConfiguracaoWhatsAppService _configuracao;
    private readonly EnvioWhatsAppService _envio;
    private readonly IdentidadeLojaService _identidade;
    private readonly ILogger<AvisoPedidoWhatsApp> _logger;

    public AvisoPedidoWhatsApp(IAppDbContext db, ConfiguracaoWhatsAppService configuracao, EnvioWhatsAppService envio,
        IdentidadeLojaService identidade, ILogger<AvisoPedidoWhatsApp> logger)
    {
        _db = db;
        _configuracao = configuracao;
        _envio = envio;
        _identidade = identidade;
        _logger = logger;
    }

    public async Task AvisarAsync(int pedidoId, CancellationToken ct = default)
    {
        try
        {
            if (!_configuracao.Licenciado) return;
            var config = await _configuracao.ObterAsync(ct);
            if (config.Provedor == ProvedorWhatsApp.Desativado) return;

            var pedido = await _db.Pedidos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
            if (pedido is null) return;
            var texto = Texto(pedido);
            if (texto is null) return;   // "aguardando pagamento": o robô já falou na hora da compra

            var conversa = await _db.ConversasWhatsApp
                .Where(c => c.UsuarioId == pedido.UsuarioId)
                .OrderByDescending(c => c.UltimaMensagemEm)
                .FirstOrDefaultAsync(ct);
            if (conversa is null) return;

            var loja = await _identidade.ObterOuPadraoAsync(ct);
            await _envio.EnviarAvisoAsync(conversa, $"*{loja.NomeLoja}*\n{texto}",
                new[] { pedido.Numero, pedido.Status.Descricao(), pedido.CodigoRastreio ?? "-" }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Aviso nunca interrompe o fluxo do pedido
            _logger.LogError(ex, "Falha ao avisar no WhatsApp o pedido {PedidoId}", pedidoId);
        }
    }

    public static string? Texto(Pedido p) => p.Status switch
    {
        StatusPedido.Pago => $"✅ Pagamento do pedido nº {p.Numero} confirmado! Total {p.Total.ToString("C", PtBr)}. Já vamos separar seus produtos.",
        StatusPedido.PagamentoEmAnalise => $"⏳ O pagamento do pedido nº {p.Numero} está em análise pela operadora. Aviso assim que for aprovado.",
        StatusPedido.EmSeparacao => $"📦 Seu pedido nº {p.Numero} está sendo separado.",
        StatusPedido.Enviado => $"🚚 Pedido nº {p.Numero} enviado! Código de rastreio: *{p.CodigoRastreio}*" +
                                $"\nAcompanhe em https://rastreamento.correios.com.br/app/index.php (ou no site da transportadora).",
        StatusPedido.Entregue => $"🎉 Pedido nº {p.Numero} entregue. Obrigado pela compra! Qualquer coisa, é só chamar por aqui.",
        StatusPedido.Cancelado => $"Pedido nº {p.Numero} cancelado. {p.MotivoCancelamento}".TrimEnd(),
        _ => null
    };
}
