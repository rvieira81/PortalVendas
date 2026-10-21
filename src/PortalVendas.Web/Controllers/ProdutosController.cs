using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Catalogo;

namespace PortalVendas.Web.Controllers;

public class ProdutosController : Controller
{
    private readonly CatalogoService _catalogo;

    public ProdutosController(CatalogoService catalogo) => _catalogo = catalogo;

    /// <summary>RF03: página do produto. Inexistente ou inativo → 404.</summary>
    [HttpGet("/produto/{slug}")]
    public async Task<IActionResult> Detalhe(string slug, CancellationToken ct)
    {
        if (slug.Length > 220) return NotFound();
        var produto = await _catalogo.ObterPorSlugAsync(slug, ct);
        return produto is null ? NotFound() : View(produto);
    }
}
