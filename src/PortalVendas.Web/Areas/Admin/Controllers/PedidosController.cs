using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Pedidos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Web.Areas.Admin.Models;

namespace PortalVendas.Web.Areas.Admin.Controllers;

public class PedidosController : AdminControllerBase
{
    private readonly PedidoService _pedidos;

    public PedidosController(PedidoService pedidos) => _pedidos = pedidos;

    /// <summary>RF18: lista com filtro por status, período e cliente/número.</summary>
    public async Task<IActionResult> Index(StatusPedido? status, string? de, string? ate, string? termo, OrigemPedido? origem, int pagina = 1,
        CancellationToken ct = default)
    {
        var filtro = new FiltroPedidosAdmin(status, LerData(de), LerData(ate), termo, pagina, origem);
        return View(new PedidosAdminIndexViewModel
        {
            Pedidos = await _pedidos.ListarAdminAsync(filtro, ct),
            Status = status,
            De = de,
            Ate = ate,
            Termo = termo,
            Origem = origem
        });
    }

    public async Task<IActionResult> Detalhe(int id, CancellationToken ct)
    {
        var detalhe = await _pedidos.ObterAdminAsync(id, ct);
        return detalhe is null ? NotFound() : View(detalhe);
    }

    [HttpPost]
    public async Task<IActionResult> AvancarStatus(int id, StatusPedido novoStatus, string? codigoRastreio, CancellationToken ct)
    {
        var r = await _pedidos.AvancarStatusAsync(id, novoStatus, codigoRastreio, Autor, ct);
        if (r.Sucesso) Sucesso($"Pedido atualizado para \"{novoStatus.Descricao()}\". O cliente foi avisado por e-mail.");
        else Erro(r.Erro);
        return RedirectToAction(nameof(Detalhe), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Cancelar(int id, string? motivo, CancellationToken ct)
    {
        var r = await _pedidos.CancelarPeloAdminAsync(id, motivo ?? string.Empty, Autor, ct);
        if (r.Sucesso) Sucesso("Pedido cancelado. Se estava pago, o estorno foi solicitado ao Mercado Pago.");
        else Erro(r.Erro);
        return RedirectToAction(nameof(Detalhe), new { id });
    }

    private static DateOnly? LerData(string? texto) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
