using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.WhatsApp;
using PortalVendas.Domain.Pedidos;

namespace PortalVendas.Application.Emails;

/// <summary>RF15: e-mail ao cliente a cada mudança de status. Nunca interrompe o fluxo principal.</summary>
public class NotificadorPedidos
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILinksAplicacao _links;
    private readonly ILogger<NotificadorPedidos> _logger;
    private readonly IAvisoPedidoWhatsApp? _whatsApp;

    public NotificadorPedidos(IAppDbContext db, IEmailSender email, ILinksAplicacao links, ILogger<NotificadorPedidos> logger,
        IAvisoPedidoWhatsApp? whatsApp = null)
    {
        _db = db;
        _email = email;
        _links = links;
        _logger = logger;
        _whatsApp = whatsApp;
    }

    public async Task NotificarStatusAsync(int pedidoId, CancellationToken ct = default)
    {
        try
        {
            var pedido = await _db.Pedidos.AsNoTracking().Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
            if (pedido is null) return;
            var usuario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == pedido.UsuarioId, ct);
            if (usuario is null || usuario.Anonimizado) return;

            // Conta criada no WhatsApp sem e-mail: o aviso vai só pela conversa
            if (!AtendimentoWhatsAppService.EhEmailInterno(usuario.Email))
            {
                var msg = ModelosEmail.MudancaStatusPedido(usuario.Nome, pedido, _links.Pedido(pedido.Id));
                await _email.EnviarAsync(usuario.Email, msg.Assunto, msg.Html, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao notificar o cliente sobre o pedido {PedidoId}", pedidoId);
        }

        if (_whatsApp is not null) await _whatsApp.AvisarAsync(pedidoId, ct);
    }
}
