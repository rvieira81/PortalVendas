using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Carrinhos;
using PortalVendas.Application.Contas;
using PortalVendas.Application.Frete;
using PortalVendas.Application.Pedidos;
using PortalVendas.Web.Infra;
using PortalVendas.Web.Models;

namespace PortalVendas.Web.Controllers;

[Authorize(Roles = Perfis.Cliente)]
[Route("checkout")]
public class CheckoutController : Controller
{
    private readonly CarrinhoService _carrinhos;
    private readonly EnderecoService _enderecos;
    private readonly CheckoutService _checkout;
    private readonly ContaService _contas;
    private readonly ContextoCarrinho _contexto;

    public CheckoutController(CarrinhoService carrinhos, EnderecoService enderecos, CheckoutService checkout, ContaService contas, ContextoCarrinho contexto)
    {
        _carrinhos = carrinhos;
        _enderecos = enderecos;
        _checkout = checkout;
        _contas = contas;
        _contexto = contexto;
    }

    /// <summary>RF09 etapa 1 e 2: endereço, frete e revisão na mesma tela (RNF11: até 3 etapas).</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(int? enderecoId, string? frete, CancellationToken ct)
    {
        var usuarioId = User.UsuarioIdObrigatorio();
        var carrinho = await _carrinhos.MontarViewAsync(_contexto.Identificacao, ct);
        if (carrinho is null || carrinho.EstaVazio) return RedirectToAction("Index", "Carrinho");
        if (!carrinho.PodeFinalizar)
        {
            TempData["Aviso"] = "Ajuste os itens indisponíveis antes de continuar.";
            return RedirectToAction("Index", "Carrinho");
        }

        var usuario = await _contas.ObterAsync(usuarioId, ct);
        var enderecos = await _enderecos.ListarAsync(usuarioId, ct);
        if (enderecos.Count == 0)
        {
            TempData["Aviso"] = "Cadastre o endereço de entrega para continuar.";
            return RedirectToAction("NovoEndereco", "MinhaConta", new { retornarPara = "/checkout" });
        }

        var selecionado = enderecos.FirstOrDefault(e => e.Id == enderecoId) ?? enderecos.FirstOrDefault(e => e.Padrao) ?? enderecos[0];
        var cotacao = await _checkout.CotarFreteAsync(usuarioId, selecionado.Cep, ct);

        return View(new CheckoutViewModel
        {
            Carrinho = carrinho,
            Enderecos = enderecos,
            EnderecoSelecionadoId = selecionado.Id,
            OpcoesFrete = cotacao.Valor ?? Array.Empty<OpcaoFrete>(),
            ErroFrete = cotacao.Sucesso ? null : cotacao.Erro,
            CodigoFreteSelecionado = frete,
            EmailConfirmado = usuario?.EmailConfirmado ?? false
        });
    }

    /// <summary>RF09 etapa final: cria o pedido e segue para o pagamento.</summary>
    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar(ConfirmarPedidoInput entrada, CancellationToken ct)
    {
        if (!ModelState.IsValid || entrada.ChaveIdempotencia == Guid.Empty)
        {
            TempData["Erro"] = "Escolha o endereço e a forma de entrega.";
            return RedirectToAction(nameof(Index), new { enderecoId = entrada.EnderecoId });
        }

        var resultado = await _checkout.CriarPedidoAsync(
            new ComandoCriarPedido(User.UsuarioIdObrigatorio(), entrada.ChaveIdempotencia, entrada.EnderecoId, entrada.CodigoFrete), ct);

        if (!resultado.Sucesso)
        {
            TempData["Erro"] = resultado.Erro;
            return RedirectToAction(nameof(Index), new { enderecoId = entrada.EnderecoId });
        }
        return RedirectToAction("Pagamento", "Pedidos", new { id = resultado.Valor });
    }
}
