using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Catalogo;
using PortalVendas.Web.Infra;
using PortalVendas.Web.Models;

namespace PortalVendas.Web.Controllers;

public class HomeController : Controller
{
    private readonly CatalogoService _catalogo;
    private readonly ILogger<HomeController> _logger;

    public HomeController(CatalogoService catalogo, ILogger<HomeController> logger)
    {
        _catalogo = catalogo;
        _logger = logger;
    }

    /// <summary>RF01/RF02: vitrine com busca, filtros e ordenação.</summary>
    [HttpGet("/")]
    [HttpGet("/busca")]
    [HttpGet("/categoria/{categoria}")]
    public async Task<IActionResult> Index(string? q, string? categoria, string? min, string? max, OrdemVitrine ordem = OrdemVitrine.Recentes,
        int pagina = 1, CancellationToken ct = default)
    {
        decimal? precoMin = Formatacao.TentarLerDecimal(min, out var vMin) ? vMin : null;
        decimal? precoMax = Formatacao.TentarLerDecimal(max, out var vMax) ? vMax : null;

        var produtos = await _catalogo.ListarVitrineAsync(new FiltroVitrine(q, categoria, precoMin, precoMax, ordem, pagina), ct);
        var categorias = await _catalogo.ListarCategoriasAsync(ct);

        if (!string.IsNullOrEmpty(categoria) && categorias.All(c => c.Slug != categoria))
            return NotFound();

        return View(new VitrineViewModel
        {
            Produtos = produtos,
            Categorias = categorias,
            Termo = q,
            Categoria = categoria,
            PrecoMin = min,
            PrecoMax = max,
            Ordem = ordem
        });
    }

    [HttpGet("/privacidade")]
    public IActionResult Privacidade() => View();

    [HttpGet("/termos")]
    public IActionResult Termos() => View();

    [Route("/erro/{codigo:int?}")]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Erro(int? codigo)
    {
        var excecao = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (excecao is not null)
            _logger.LogError(excecao.Error, "Erro não tratado em {Caminho} (RequestId {RequestId})", excecao.Path, HttpContext.TraceIdentifier);

        var status = codigo ?? 500;
        Response.StatusCode = status;
        return View("Erro", new ErroViewModel { Codigo = status, RequestId = HttpContext.TraceIdentifier });
    }
}
