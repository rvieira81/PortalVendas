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
