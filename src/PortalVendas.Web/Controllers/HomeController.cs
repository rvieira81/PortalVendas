using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Catalogo;
using PortalVendas.Web.Infra;
using PortalVendas.Web.Models;

namespace PortalVendas.Web.Controllers;

public class HomeController : Controller
{
    private readonly CatalogoService _catalogo;
    private readonly ConfiguracaoLojaService _configuracao;
    private readonly IRelogio _relogio;
    private readonly ILogger<HomeController> _logger;

    public HomeController(CatalogoService catalogo, ConfiguracaoLojaService configuracao, IRelogio relogio, ILogger<HomeController> logger)
    {
        _catalogo = catalogo;
        _configuracao = configuracao;
        _relogio = relogio;
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

        ViewData["ConfigLoja"] = await _configuracao.ObterAsync(ct);

        // Página inicial com banner e seções só em "/" sem filtros; "/busca" sem termo lista todos os produtos
        var inicio = Request.Path == "/" && string.IsNullOrEmpty(q) && string.IsNullOrEmpty(categoria) && string.IsNullOrEmpty(min) && string.IsNullOrEmpty(max)
                     && produtos.PaginaAtual == 1 && ordem == OrdemVitrine.Recentes;
        if (inicio)
        {
            // Consultas sequenciais: o DbContext não aceita operações em paralelo
            var ofertas = await _catalogo.ListarOfertasAsync(10, ct);
            var maisVendidos = await _catalogo.ListarMaisVendidosAsync(_relogio.AgoraUtc, 10, ct);
            var novidades = await _catalogo.ListarNovidadesAsync(10, ct);
            var comFoto = await _catalogo.ListarCategoriasComFotoAsync(ct);
            return View(new VitrineViewModel
            {
                Produtos = produtos,
                Categorias = categorias,
                Ordem = ordem,
                Inicio = true,
                Ofertas = ofertas,
                MaisVendidos = maisVendidos,
                Novidades = novidades,
                CategoriasComFoto = comFoto
            });
        }

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
