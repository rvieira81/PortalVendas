using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Carrinhos;

namespace PortalVendas.Web.Infra;

/// <summary>Contador de itens do carrinho no topo da página.</summary>
public class ResumoCarrinhoViewComponent : ViewComponent
{
    private readonly CarrinhoService _carrinhos;
    private readonly ContextoCarrinho _contexto;

    public ResumoCarrinhoViewComponent(CarrinhoService carrinhos, ContextoCarrinho contexto)
    {
        _carrinhos = carrinhos;
        _contexto = contexto;
    }

    public async Task<IViewComponentResult> InvokeAsync() =>
        View(await _carrinhos.ContarItensAsync(_contexto.Identificacao, HttpContext.RequestAborted));
}

/// <summary>Modelo do componente de paginação.</summary>
public record Paginacao(int PaginaAtual, int TotalPaginas, string UrlBase)
{
    public string Url(int pagina) => UrlBase + (UrlBase.Contains('?') ? "&" : "?") + "pagina=" + pagina;
}

/// <summary>
/// Gaveta lateral exibida logo depois de "Adicionar ao carrinho" (TempData["MiniCarrinho"]):
/// o cliente vê o que tem no carrinho e quanto falta para o frete grátis sem sair da página.
/// </summary>
public class MiniCarrinhoViewComponent : ViewComponent
{
    private readonly CarrinhoService _carrinhos;
    private readonly ContextoCarrinho _contexto;
    private readonly PortalVendas.Application.Admin.ConfiguracaoLojaService _configuracao;

    public MiniCarrinhoViewComponent(CarrinhoService carrinhos, ContextoCarrinho contexto,
        PortalVendas.Application.Admin.ConfiguracaoLojaService configuracao)
    {
        _carrinhos = carrinhos;
        _contexto = contexto;
        _configuracao = configuracao;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var ct = HttpContext.RequestAborted;
        var carrinho = await _carrinhos.MontarViewAsync(_contexto.Identificacao, ct);
        if (carrinho is null || carrinho.Itens.Count == 0) return Content(string.Empty);
        return View(new MiniCarrinhoModelo(carrinho, await _configuracao.ObterAsync(ct)));
    }
}

public record MiniCarrinhoModelo(CarrinhoView Carrinho, PortalVendas.Application.Admin.ConfiguracaoLoja Configuracao)
{
    public decimal FaltaFreteGratis => Configuracao.TemFreteGratis ? Math.Max(0, Configuracao.FreteGratisAPartirDe - Carrinho.Subtotal) : 0;
    public int PercentualFreteGratis => !Configuracao.TemFreteGratis ? 0
        : (int)Math.Clamp(Math.Floor(Carrinho.Subtotal / Configuracao.FreteGratisAPartirDe * 100), 0, 100);
}
