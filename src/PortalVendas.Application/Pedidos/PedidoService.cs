using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Emails;
using PortalVendas.Application.Pagamentos;
using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Pagamentos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Domain.Usuarios;

namespace PortalVendas.Application.Pedidos;

public record PedidoResumo(int Id, DateTime CriadoEm, StatusPedido Status, decimal Total, int QuantidadeItens)
{
    public string Numero => Id.ToString("D6");
}

public record FiltroPedidosAdmin(StatusPedido? Status = null, DateOnly? De = null, DateOnly? Ate = null, string? Termo = null, int Pagina = 1,
    OrigemPedido? Origem = null);

public record PedidoAdminResumo(int Id, DateTime CriadoEm, string Cliente, StatusPedido Status, decimal Total, string FreteDescricao,
    OrigemPedido Origem = OrigemPedido.Site)
{
    public string Numero => Id.ToString("D6");
}

public record PedidoAdminDetalhe(Pedido Pedido, Usuario Cliente);

public class PedidoService
{
    public const int TamanhoPagina = 20;

    private readonly IAppDbContext _db;
    private readonly IGatewayPagamento _gateway;
    private readonly EstoqueService _estoque;
    private readonly NotificadorPedidos _notificador;
    private readonly IRelogio _relogio;
    private readonly ILogger<PedidoService> _logger;

    public PedidoService(IAppDbContext db, IGatewayPagamento gateway, EstoqueService estoque, NotificadorPedidos notificador,
        IRelogio relogio, ILogger<PedidoService> logger)
    {
        _db = db;
        _gateway = gateway;
        _estoque = estoque;
        _notificador = notificador;
        _relogio = relogio;
        _logger = logger;
    }

    // ================================================================== cliente (RF14)

    public async Task<Pagina<PedidoResumo>> ListarDoClienteAsync(int usuarioId, int pagina, CancellationToken ct = default)
    {
        pagina = Pagina<PedidoResumo>.Normalizar(pagina);
        var consulta = _db.Pedidos.AsNoTracking().Where(p => p.UsuarioId == usuarioId);
        var total = await consulta.CountAsync(ct);
        var itens = await consulta
            .OrderByDescending(p => p.CriadoEm)
            .Skip((pagina - 1) * TamanhoPagina)
            .Take(TamanhoPagina)
            .Select(p => new PedidoResumo(p.Id, p.CriadoEm, p.Status, p.Total, p.Itens.Sum(i => i.Quantidade)))
            .ToListAsync(ct);
        return new Pagina<PedidoResumo>(itens, pagina, TamanhoPagina, total);
    }

    /// <summary>Sempre filtra pelo dono: pedido de outro cliente → null → 404 (proteção contra IDOR).</summary>
    public Task<Pedido?> ObterDoClienteAsync(int usuarioId, int pedidoId, CancellationToken ct = default) =>
        _db.Pedidos.AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .Include(p => p.Historico)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == pedidoId && p.UsuarioId == usuarioId, ct);

    public async Task<(StatusPedido Status, bool Pago)?> ObterStatusDoClienteAsync(int usuarioId, int pedidoId, CancellationToken ct = default)
    {
        var status = await _db.Pedidos.AsNoTracking()
            .Where(p => p.Id == pedidoId && p.UsuarioId == usuarioId)
            .Select(p => (StatusPedido?)p.Status)
            .FirstOrDefaultAsync(ct);
        if (status is null) return null;
        var pago = status.Value is StatusPedido.Pago or StatusPedido.EmSeparacao or StatusPedido.Enviado or StatusPedido.Entregue;
        return (status.Value, pago);
    }

    /// <summary>RF16: cancelamento pelo cliente enquanto não enviado.</summary>
    public Task<Resultado> CancelarPeloClienteAsync(int usuarioId, int pedidoId, CancellationToken ct = default) =>
        CancelarAsync(pedidoId, usuarioId, "Cancelado pelo cliente", Autores.Cliente(usuarioId), ct);

    // ================================================================== administrador (RF18)

    public async Task<Pagina<PedidoAdminResumo>> ListarAdminAsync(FiltroPedidosAdmin filtro, CancellationToken ct = default)
    {
        var pagina = Pagina<PedidoAdminResumo>.Normalizar(filtro.Pagina);
        var consulta =
            from p in _db.Pedidos.AsNoTracking()
            join u in _db.Usuarios.AsNoTracking() on p.UsuarioId equals u.Id
            select new { Pedido = p, Usuario = u };

        if (filtro.Status is not null)
            consulta = consulta.Where(x => x.Pedido.Status == filtro.Status);
        if (filtro.Origem is not null)
            consulta = consulta.Where(x => x.Pedido.Origem == filtro.Origem);
        if (filtro.De is not null)
        {
            var de = Horario.DeBrasiliaParaUtc(filtro.De.Value.ToDateTime(TimeOnly.MinValue));
            consulta = consulta.Where(x => x.Pedido.CriadoEm >= de);
        }
        if (filtro.Ate is not null)
        {
            var ate = Horario.DeBrasiliaParaUtc(filtro.Ate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
            consulta = consulta.Where(x => x.Pedido.CriadoEm < ate);
        }
        var termo = filtro.Termo?.Trim();
        if (!string.IsNullOrEmpty(termo))
        {
            if (int.TryParse(termo.TrimStart('#'), out var numero))
                consulta = consulta.Where(x => x.Pedido.Id == numero);
            else
                consulta = consulta.Where(x => x.Usuario.Nome.Contains(termo) || x.Usuario.Email.Contains(termo.ToLower()));
        }

        var total = await consulta.CountAsync(ct);
        var itens = await consulta
            .OrderByDescending(x => x.Pedido.CriadoEm)
            .Skip((pagina - 1) * TamanhoPagina)
            .Take(TamanhoPagina)
            .Select(x => new PedidoAdminResumo(x.Pedido.Id, x.Pedido.CriadoEm, x.Usuario.Nome, x.Pedido.Status, x.Pedido.Total, x.Pedido.FreteDescricao, x.Pedido.Origem))
            .ToListAsync(ct);
        return new Pagina<PedidoAdminResumo>(itens, pagina, TamanhoPagina, total);
    }

    public async Task<PedidoAdminDetalhe?> ObterAdminAsync(int pedidoId, CancellationToken ct = default)
    {
        var pedido = await _db.Pedidos.AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .Include(p => p.Historico)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null) return null;
        var cliente = await _db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == pedido.UsuarioId, ct);
        return new PedidoAdminDetalhe(pedido, cliente);
    }

    /// <summary>RF18: Pago → Em separação → Enviado (com rastreio) → Entregue.</summary>
    public async Task<Resultado> AvancarStatusAsync(int pedidoId, StatusPedido novo, string? codigoRastreio, string autor, CancellationToken ct = default)
    {
        var pedido = await _db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null) return Resultado.Falha("Pedido não encontrado.");
        if (!pedido.ProximosStatusManuais().Contains(novo))
            return Resultado.Falha($"Não é possível mudar o pedido de \"{pedido.Status.Descricao()}\" para \"{novo.Descricao()}\".");

        var agora = _relogio.AgoraUtc;
        try
        {
            switch (novo)
            {
                case StatusPedido.EmSeparacao: pedido.IniciarSeparacao(agora, autor); break;
                case StatusPedido.Enviado: pedido.MarcarEnviado(codigoRastreio ?? string.Empty, agora, autor); break;
                case StatusPedido.Entregue: pedido.MarcarEntregue(agora, autor); break;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            _db.ChangeTracker.Clear();
            return Resultado.Falha(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return Resultado.Falha("O pedido foi alterado enquanto você editava. Recarregue a página e tente de novo.");
        }

        await _notificador.NotificarStatusAsync(pedido.Id, ct);
        return Resultado.Ok();
    }

    public Task<Resultado> CancelarPeloAdminAsync(int pedidoId, string motivo, string autor, CancellationToken ct = default) =>
        CancelarAsync(pedidoId, null, string.IsNullOrWhiteSpace(motivo) ? "Cancelado pela loja" : motivo, autor, ct);

    // ================================================================== cancelamento

    private async Task<Resultado> CancelarAsync(int pedidoId, int? usuarioIdDono, string motivo, string autor, CancellationToken ct)
    {
        var pedido = await _db.Pedidos.Include(p => p.Itens).Include(p => p.Pagamentos)
            .FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null || (usuarioIdDono is not null && pedido.UsuarioId != usuarioIdDono))
            return Resultado.Falha("Pedido não encontrado.");

        if (pedido.Status == StatusPedido.PagamentoEmAnalise)
            return Resultado.Falha("O pagamento está em análise pela operadora. Aguarde a resposta para cancelar.");
        if (usuarioIdDono is not null && !pedido.PodeSerCanceladoPeloCliente)
            return Resultado.Falha("Este pedido não pode mais ser cancelado. Fale com nosso atendimento.");
        if (!Pedido.TransicaoPermitida(pedido.Status, StatusPedido.Cancelado))
            return Resultado.Falha($"Pedido \"{pedido.Status.Descricao()}\" não pode ser cancelado.");

        var agora = _relogio.AgoraUtc;
        var estoqueBaixado = pedido.EstoqueBaixado;
        var aguardando = pedido.Status == StatusPedido.AguardandoPagamento;

        // Estorno antes de mudar o pedido: se o gateway falhar, nada muda e o cliente pode tentar de novo
        var aprovado = pedido.Pagamentos.FirstOrDefault(p => p.Status == StatusPagamento.Aprovado);
        if (aprovado is not null)
        {
            try
            {
                await _gateway.EstornarAsync(aprovado.GatewayId!.Value, aprovado.ChaveIdempotencia, ct);
                aprovado.MarcarEstornado(agora);
            }
            catch (GatewayPagamentoException ex)
            {
                _logger.LogError(ex, "Falha ao estornar o pagamento {GatewayId} do pedido {PedidoId}", aprovado.GatewayId, pedido.Id);
                return Resultado.Falha("Não foi possível solicitar o estorno agora. Tente novamente em alguns minutos.");
            }
        }

        try
        {
            var estrategia = _db.Database.CreateExecutionStrategy();
            await estrategia.ExecuteAsync(async () =>
            {
                await using var transacao = await _db.Database.BeginTransactionAsync(ct);
                if (estoqueBaixado) await _estoque.DevolverAsync(pedido, ct);
                else if (aguardando) await _estoque.LiberarReservaAsync(pedido, ct);

                var texto = aprovado is null ? motivo : $"{motivo}. Estorno de {aprovado.Valor:C} solicitado ao Mercado Pago.";
                pedido.Cancelar(texto, agora, autor);
                await _db.SaveChangesAsync(ct);
                await transacao.CommitAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            if (aprovado is not null)
                _logger.LogCritical("Pedido {PedidoId}: estorno feito mas o cancelamento conflitou com outra alteração. Revisar manualmente.", pedido.Id);
            return Resultado.Falha("O pedido foi alterado por outro processo. Recarregue a página e confira o status.");
        }

        _logger.LogInformation("Pedido {PedidoId} cancelado por {Autor}", pedido.Id, autor);
        await _notificador.NotificarStatusAsync(pedido.Id, ct);
        return Resultado.Ok();
    }
}
