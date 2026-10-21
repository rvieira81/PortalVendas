using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Contas;
using PortalVendas.Application.Pagamentos;
using PortalVendas.Application.Pedidos;
using PortalVendas.Domain.Pagamentos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Infrastructure.Pagamentos;
using PortalVendas.Web.Infra;
using PortalVendas.Web.Models;

namespace PortalVendas.Web.Controllers;

[Authorize(Roles = Perfis.Cliente)]
[Route("pedidos")]
public class PedidosController : Controller
{
    private readonly PedidoService _pedidos;
    private readonly PagamentoService _pagamentos;
    private readonly ContaService _contas;
    private readonly MercadoPagoOptions _mercadoPago;
    private readonly IRelogio _relogio;

    public PedidosController(PedidoService pedidos, PagamentoService pagamentos, ContaService contas,
        IOptions<MercadoPagoOptions> mercadoPago, IRelogio relogio)
    {
        _pedidos = pedidos;
        _pagamentos = pagamentos;
        _contas = contas;
        _mercadoPago = mercadoPago.Value;
        _relogio = relogio;
    }

    private int UsuarioId => User.UsuarioIdObrigatorio();

    // ================================================================== RF14

    [HttpGet("")]
    public async Task<IActionResult> Index(int pagina = 1, CancellationToken ct = default) =>
        View(new MeusPedidosViewModel { Pedidos = await _pedidos.ListarDoClienteAsync(UsuarioId, pagina, ct) });

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detalhe(int id, CancellationToken ct)
    {
        var pedido = await _pedidos.ObterDoClienteAsync(UsuarioId, id, ct);
        return pedido is null ? NotFound() : View(pedido);
    }

    [HttpPost("{id:int}/cancelar")]
    public async Task<IActionResult> Cancelar(int id, CancellationToken ct)
    {
        var resultado = await _pedidos.CancelarPeloClienteAsync(UsuarioId, id, ct);
        TempData[resultado.Sucesso ? "Sucesso" : "Erro"] = resultado.Sucesso
            ? "Pedido cancelado. Se já estava pago, o estorno foi solicitado e aparece na fatura ou conta em alguns dias."
            : resultado.Erro;
        return RedirectToAction(nameof(Detalhe), new { id });
    }

    // ================================================================== pagamento (RF10, RF11, RF12)

    [HttpGet("{id:int}/pagamento")]
    public async Task<IActionResult> Pagamento(int id, string? aba, CancellationToken ct)
    {
        var pedido = await _pagamentos.ObterPedidoParaPagamentoAsync(UsuarioId, id, ct);
        if (pedido is null) return NotFound();
        if (!pedido.PodeReceberPagamento) return RedirectToAction(nameof(Detalhe), new { id });

        var agora = _relogio.AgoraUtc;
        var usuario = await _contas.ObterAsync(UsuarioId, ct);
        return View(new PagamentoViewModel
        {
            Pedido = pedido,
            PixVigente = pedido.Pagamentos.Where(p => p.Metodo == MetodoPagamento.Pix && p.PixOuBoletoAindaValido(agora))
                .OrderByDescending(p => p.CriadoEm).FirstOrDefault(),
            BoletoVigente = pedido.Pagamentos.Where(p => p.Metodo == MetodoPagamento.Boleto && p.PixOuBoletoAindaValido(agora))
                .OrderByDescending(p => p.CriadoEm).FirstOrDefault(),
            PublicKey = _mercadoPago.PublicKey,
            ParcelasMaximas = await _pagamentos.ParcelasPermitidasAsync(pedido.Total, ct),
            EmailPagador = usuario?.Email ?? string.Empty,
            AbaInicial = aba
        });
    }

    [HttpPost("{id:int}/pagamento/pix")]
    public async Task<IActionResult> GerarPix(int id, CancellationToken ct)
    {
        var resultado = await _pagamentos.GerarPixAsync(UsuarioId, id, ct);
        if (!resultado.Sucesso) TempData["Erro"] = resultado.Erro;
        return RedirectToAction(nameof(Pagamento), new { id, aba = "pix" });
    }

    [HttpPost("{id:int}/pagamento/boleto")]
    public async Task<IActionResult> GerarBoleto(int id, CancellationToken ct)
    {
        var resultado = await _pagamentos.GerarBoletoAsync(UsuarioId, id, ct);
        if (!resultado.Sucesso) TempData["Erro"] = resultado.Erro;
        return RedirectToAction(nameof(Pagamento), new { id, aba = "boleto" });
    }

    /// <summary>Recebe o token do Card Payment Brick (JSON) e responde JSON.</summary>
    [HttpPost("{id:int}/pagamento/cartao")]
    [EnableRateLimiting("consulta")]
    public async Task<IActionResult> PagarCartao(int id, [FromBody] DadosCartaoInput entrada, CancellationToken ct)
    {
        var emissor = entrada.IssuerId switch
        {
            { ValueKind: System.Text.Json.JsonValueKind.String } e => e.GetString(),
            { ValueKind: System.Text.Json.JsonValueKind.Number } e => e.GetRawText(),
            _ => null
        };

        var resultado = await _pagamentos.PagarComCartaoAsync(UsuarioId, id, new DadosCartao(
            entrada.Token ?? string.Empty,
            entrada.PaymentMethodId ?? string.Empty,
            emissor,
            entrada.Installments,
            entrada.Payer?.Identification?.Type,
            entrada.Payer?.Identification?.Number), ct);

        if (!resultado.Sucesso)
            return Json(new { sucesso = false, mensagem = resultado.Erro });

        return Json(new
        {
            sucesso = true,
            aprovado = resultado.Valor!.Aprovado,
            mensagem = resultado.Valor.Mensagem,
            redirecionar = Url.Action(nameof(Detalhe), new { id })
        });
    }

    /// <summary>Consulta leve para a tela do Pix atualizar sozinha quando o pagamento cair.</summary>
    [HttpGet("{id:int}/status")]
    [EnableRateLimiting("consulta")]
    public async Task<IActionResult> Status(int id, CancellationToken ct)
    {
        var status = await _pedidos.ObterStatusDoClienteAsync(UsuarioId, id, ct);
        if (status is null) return NotFound();
        return Json(new { status = status.Value.Status.Descricao(), pago = status.Value.Pago });
    }
}
