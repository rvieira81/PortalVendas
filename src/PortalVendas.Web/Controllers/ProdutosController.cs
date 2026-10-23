using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Catalogo;
using PortalVendas.Web.Models;

namespace PortalVendas.Web.Controllers;

public class ProdutosController : Controller
{
    private readonly CatalogoService _catalogo;
    private readonly ConfiguracaoLojaService _configuracao;

    public ProdutosController(CatalogoService catalogo, ConfiguracaoLojaService configuracao)
    {
        _catalogo = catalogo;
        _configuracao = configuracao;
    }

    /// <summary>RF03: página do produto. Inexistente ou inativo → 404.</summary>
    [HttpGet("/produto/{slug}")]
    public async Task<IActionResult> Detalhe(string slug, CancellationToken ct)
    {
        if (slug.Length > 220) return NotFound();
        var produto = await _catalogo.ObterPorSlugAsync(slug, ct);
        if (produto is null) return NotFound();

        var configuracao = await _configuracao.ObterAsync(ct);
        ViewData["ConfigLoja"] = configuracao;
        return View(new ProdutoPaginaViewModel
        {
            Produto = produto,
            Configuracao = configuracao,
            Relacionados = await _catalogo.ListarRelacionadosAsync(produto.Categoria.Id, produto.Id, 4, ct)
        });
    }
}
