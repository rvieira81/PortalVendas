using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Carrinhos;
using PortalVendas.Web.Infra;

namespace PortalVendas.Web.Controllers;

[Route("carrinho")]
public class CarrinhoController : Controller
{
    private readonly CarrinhoService _carrinhos;
    private readonly ContextoCarrinho _contexto;

    public CarrinhoController(CarrinhoService carrinhos, ContextoCarrinho contexto)
    {
        _carrinhos = carrinhos;
        _contexto = contexto;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var view = await _carrinhos.MontarViewAsync(_contexto.Identificacao, ct);
        return View(view);
    }

    /// <summary>RF04</summary>
    [HttpPost("adicionar")]
    public async Task<IActionResult> Adicionar(int produtoId, int quantidade = 1, string? retornarPara = null, bool abrirMini = false, CancellationToken ct = default)
    {
        var resultado = await _carrinhos.AdicionarAsync(_contexto.Identificacao, produtoId, quantidade, ct);
        if (resultado.Sucesso)
        {
            _contexto.GravarToken(resultado.Valor);
            // Com o mini carrinho aberto, a confirmação já aparece nele: só avisos vão para a faixa de mensagens
            if (abrirMini && resultado.Erro is null)
                TempData["MiniCarrinho"] = true;
            else
                TempData[resultado.Erro is null ? "Sucesso" : "Aviso"] = resultado.Erro ?? "Produto adicionado ao carrinho.";
        }
        else
        {
            TempData["Erro"] = resultado.Erro;
        }

        if (!string.IsNullOrEmpty(retornarPara) && Url.IsLocalUrl(retornarPara))
            return LocalRedirect(retornarPara);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("alterar")]
    public async Task<IActionResult> Alterar(int produtoId, int quantidade, CancellationToken ct)
    {
        var resultado = await _carrinhos.AlterarQuantidadeAsync(_contexto.Identificacao, produtoId, quantidade, ct);
        if (!resultado.Sucesso) TempData["Aviso"] = resultado.Erro;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("remover")]
    public async Task<IActionResult> Remover(int produtoId, CancellationToken ct)
    {
        await _carrinhos.RemoverAsync(_contexto.Identificacao, produtoId, ct);
        TempData["Sucesso"] = "Item removido.";
        return RedirectToAction(nameof(Index));
    }
}
