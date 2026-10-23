using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Contas;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Web.Areas.Admin.Models;
using PortalVendas.Web.Infra;

namespace PortalVendas.Web.Areas.Admin.Controllers;

/// <summary>Configuração do segundo fator (TOTP) no primeiro acesso do administrador.</summary>
[Area("Admin")]
[Authorize(Policy = Politicas.AdminConfigurarMfa)]
public class SegurancaController : Controller
{
    private readonly ContaService _contas;
    private readonly ServicoSessao _sessao;
    private readonly ILicencaLoja _licenca;

    public SegurancaController(ContaService contas, ServicoSessao sessao, ILicencaLoja licenca)
    {
        _contas = contas;
        _sessao = sessao;
        _licenca = licenca;
    }

    public async Task<IActionResult> Configurar(CancellationToken ct)
    {
        var segredo = await _contas.PrepararTotpAsync(User.UsuarioIdObrigatorio(), ct);
        if (segredo is null)
        {
            // Já configurado: se a sessão ainda não tem o segundo fator, precisa entrar de novo
            if (!User.HasClaim(Reivindicacoes.MetodoAutenticacao, Reivindicacoes.Mfa))
            {
                await ServicoSessao.SairAsync(HttpContext);
                return RedirectToAction("Entrar", "Conta", new { area = "" });
            }
            return RedirectToAction("Index", "Painel");
        }
        return View(MontarModelo(segredo));
    }

    [HttpPost]
    public async Task<IActionResult> Configurar(string codigo, CancellationToken ct)
    {
        var usuarioId = User.UsuarioIdObrigatorio();
        var r = await _contas.AtivarTotpAsync(usuarioId, codigo, ct);
        if (!r.Sucesso)
        {
            ModelState.AddModelError(string.Empty, r.Erro!);
            var segredo = await _contas.PrepararTotpAsync(usuarioId, ct);
            if (segredo is null) return RedirectToAction(nameof(Configurar));
            return View(MontarModelo(segredo));
        }

        var usuario = await _contas.ObterAsync(usuarioId, ct);
        await _sessao.EntrarAsync(HttpContext, usuario!, comMfa: true, lembrar: false);
        TempData["Sucesso"] = "Segundo fator ativado. A partir de agora, o login do painel pede o código do aplicativo.";
        return RedirectToAction("Index", "Painel");
    }

    private ConfigurarMfaViewModel MontarModelo(string segredo) => new()
    {
        Segredo = segredo,
        SegredoFormatado = Totp.FormatarParaLeitura(segredo),
        UriOtpAuth = Totp.MontarUri(_licenca.Estado.Ativa ? _licenca.Estado.Dados!.NomeLoja : "Portal de Vendas", User.Autor(), segredo)
    };
}
