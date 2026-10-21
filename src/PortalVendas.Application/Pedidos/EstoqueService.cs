using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Pedidos;

namespace PortalVendas.Application.Pedidos;

/// <summary>
/// Operações de estoque como UPDATE atômico no banco (RN02/RN03). Devem rodar dentro da transação
/// que muda o status do pedido, para que as duas coisas aconteçam juntas ou nenhuma.
/// </summary>
public class EstoqueService
{
    private readonly IAppDbContext _db;

    public EstoqueService(IAppDbContext db) => _db = db;

    /// <summary>Reserva se houver disponível. False = sem estoque (nenhuma linha afetada).</summary>
    public async Task<bool> ReservarAsync(int produtoId, int quantidade, CancellationToken ct = default)
    {
        var linhas = await _db.Produtos
            .Where(p => p.Id == produtoId && p.Ativo && p.Estoque - p.EstoqueReservado >= quantidade)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.EstoqueReservado, p => p.EstoqueReservado + quantidade), ct);
        return linhas == 1;
    }

    /// <summary>Pedido expirou ou foi cancelado antes de pagar: devolve a reserva.</summary>
    public async Task LiberarReservaAsync(Pedido pedido, CancellationToken ct = default)
    {
        foreach (var item in pedido.Itens)
        {
            var qtd = item.Quantidade;
            await _db.Produtos
                .Where(p => p.Id == item.ProdutoId && p.EstoqueReservado >= qtd)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EstoqueReservado, p => p.EstoqueReservado - qtd), ct);
        }
    }

    /// <summary>Pagamento aprovado: a reserva vira baixa definitiva.</summary>
    public async Task BaixarReservaAsync(Pedido pedido, CancellationToken ct = default)
    {
        foreach (var item in pedido.Itens)
        {
            var qtd = item.Quantidade;
            var linhas = await _db.Produtos
                .Where(p => p.Id == item.ProdutoId && p.EstoqueReservado >= qtd && p.Estoque >= qtd)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Estoque, p => p.Estoque - qtd)
                    .SetProperty(p => p.EstoqueReservado, p => p.EstoqueReservado - qtd), ct);
            if (linhas != 1)
                throw new DomainException($"Reserva de estoque inconsistente para o produto {item.ProdutoId} no pedido {pedido.Id}.");
        }
    }

    /// <summary>Pedido pago cancelado antes do envio: a mercadoria volta ao estoque.</summary>
    public async Task DevolverAsync(Pedido pedido, CancellationToken ct = default)
    {
        foreach (var item in pedido.Itens)
        {
            var qtd = item.Quantidade;
            await _db.Produtos
                .Where(p => p.Id == item.ProdutoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estoque, p => p.Estoque + qtd), ct);
        }
    }
}
