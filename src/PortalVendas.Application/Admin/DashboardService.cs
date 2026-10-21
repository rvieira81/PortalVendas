using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Pedidos;

namespace PortalVendas.Application.Admin;

public record ProdutoMaisVendido(int ProdutoId, string Nome, int Quantidade, decimal Faturamento);

public record PainelVendas(
    DateOnly De, DateOnly Ate, decimal Faturamento, int PedidosPagos, decimal TicketMedio,
    int AguardandoPagamento, int ParaSeparar, int ParaEnviar, IReadOnlyList<ProdutoMaisVendido> MaisVendidos,
    IReadOnlyList<(DateOnly Dia, decimal Valor)> PorDia);

/// <summary>RF20: indicadores de vendas por período (datas no horário de Brasília).</summary>
public class DashboardService
{
    private static readonly StatusPedido[] StatusFaturados =
        { StatusPedido.Pago, StatusPedido.EmSeparacao, StatusPedido.Enviado, StatusPedido.Entregue };

    private readonly IAppDbContext _db;
    private readonly IRelogio _relogio;

    public DashboardService(IAppDbContext db, IRelogio relogio)
    {
        _db = db;
        _relogio = relogio;
    }

    public async Task<PainelVendas> ObterAsync(DateOnly? de, DateOnly? ate, CancellationToken ct = default)
    {
        var hoje = Horario.HojeEmBrasilia(_relogio.AgoraUtc);
        var fim = ate ?? hoje;
        var inicio = de ?? fim.AddDays(-29);
        if (inicio > fim) (inicio, fim) = (fim, inicio);

        var inicioUtc = Horario.DeBrasiliaParaUtc(inicio.ToDateTime(TimeOnly.MinValue));
        var fimUtc = Horario.DeBrasiliaParaUtc(fim.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var pagos = _db.Pedidos.AsNoTracking()
            .Where(p => p.PagoEm >= inicioUtc && p.PagoEm < fimUtc && StatusFaturados.Contains(p.Status));

        var linhas = await pagos.Select(p => new { p.PagoEm, p.Total }).ToListAsync(ct);
        var faturamento = linhas.Sum(l => l.Total);
        var quantidade = linhas.Count;

        var porDia = linhas
            .GroupBy(l => DateOnly.FromDateTime(Horario.ParaBrasilia(l.PagoEm!.Value)))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Total));
        var serie = new List<(DateOnly, decimal)>();
        for (var d = inicio; d <= fim; d = d.AddDays(1)) serie.Add((d, porDia.GetValueOrDefault(d)));

        var maisVendidos = await (
                from i in _db.PedidoItens.AsNoTracking()
                join p in pagos on i.PedidoId equals p.Id
                group i by new { i.ProdutoId, i.NomeProduto } into g
                select new { g.Key.ProdutoId, g.Key.NomeProduto, Quantidade = g.Sum(x => x.Quantidade), Valor = g.Sum(x => x.Total) })
            .OrderByDescending(x => x.Quantidade)
            .Take(5)
            .ToListAsync(ct);

        var contagens = await _db.Pedidos.AsNoTracking()
            .Where(p => p.Status == StatusPedido.AguardandoPagamento || p.Status == StatusPedido.Pago || p.Status == StatusPedido.EmSeparacao)
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Quantidade = g.Count() })
            .ToListAsync(ct);
        int Contar(StatusPedido s) => contagens.FirstOrDefault(c => c.Status == s)?.Quantidade ?? 0;

        return new PainelVendas(
            inicio, fim, faturamento, quantidade, quantidade == 0 ? 0 : Math.Round(faturamento / quantidade, 2),
            Contar(StatusPedido.AguardandoPagamento), Contar(StatusPedido.Pago), Contar(StatusPedido.EmSeparacao),
            maisVendidos.Select(m => new ProdutoMaisVendido(m.ProdutoId, m.NomeProduto, m.Quantidade, m.Valor)).ToList(),
            serie);
    }
}
