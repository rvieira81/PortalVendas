using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Pagamentos;

namespace PortalVendas.Application.Pedidos;

/// <summary>Rotinas periódicas: expiração de pedidos, conciliação de pagamentos e limpeza (ADR-007).</summary>
public class ManutencaoService
{
    private readonly IAppDbContext _db;
    private readonly PagamentoService _pagamentos;
    private readonly IRelogio _relogio;
    private readonly ILogger<ManutencaoService> _logger;

    public ManutencaoService(IAppDbContext db, PagamentoService pagamentos, IRelogio relogio, ILogger<ManutencaoService> logger)
    {
        _db = db;
        _pagamentos = pagamentos;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken ct)
    {
        var conciliados = await _pagamentos.ConciliarPendentesAsync(ct);
        var expirados = await _pagamentos.ExpirarPedidosAsync(ct);
        var limpos = await LimparAsync(ct);

        if (conciliados + expirados + limpos > 0)
            _logger.LogInformation("Manutenção: {Conciliados} pagamento(s) conciliado(s), {Expirados} pedido(s) expirado(s), {Limpos} registro(s) antigo(s) removido(s)",
                conciliados, expirados, limpos);
    }

    /// <summary>Retenção (ver docs/03-banco.md): carrinhos anônimos abandonados e tokens vencidos.</summary>
    private async Task<int> LimparAsync(CancellationToken ct)
    {
        var agora = _relogio.AgoraUtc;
        var carrinhosAntigos = agora.AddDays(-30);
        var tokensAntigos = agora.AddDays(-7);
        var notificacoesAntigas = agora.AddYears(-1);

        var total = await _db.Carrinhos.Where(c => c.UsuarioId == null && c.AtualizadoEm < carrinhosAntigos).ExecuteDeleteAsync(ct);
        total += await _db.TokensUsuario.Where(t => t.ExpiraEm < tokensAntigos).ExecuteDeleteAsync(ct);
        total += await _db.NotificacoesGateway.Where(n => n.RecebidaEm < notificacoesAntigas).ExecuteDeleteAsync(ct);
        return total;
    }
}
