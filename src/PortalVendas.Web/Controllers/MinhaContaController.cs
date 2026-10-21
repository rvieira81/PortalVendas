using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Contas;
using PortalVendas.Domain.Comum;
using PortalVendas.Web.Infra;
using PortalVendas.Web.Models;

namespace PortalVendas.Web.Controllers;

[Authorize]
[Route("minha-conta")]
public class MinhaContaController : Controller
{
    private readonly ContaService _contas;
    private readonly EnderecoService _enderecos;

    public MinhaContaController(ContaService contas, EnderecoService enderecos)
    {
        _contas = contas;
        _enderecos = enderecos;
    }

    private int UsuarioId => User.UsuarioIdObrigatorio();

    // ================================================================== dados

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var u = await _contas.ObterAsync(UsuarioId, ct);
        if (u is null) return NotFound();
        ViewBag.AlterarSenha = new AlterarSenhaViewModel();
        return View(new MinhaContaViewModel
        {
            Email = u.Email,
            EmailConfirmado = u.EmailConfirmado,
            CpfMascarado = Cpf.Mascarar(u.Cpf),
            Nome = u.Nome,
            Telefone = Telefone.Formatar(u.Telefone)
        });
    }

    [HttpPost("")]
    public async Task<IActionResult> Index(MinhaContaViewModel modelo, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var resultado = await _contas.AtualizarDadosAsync(UsuarioId, modelo.Nome, modelo.Telefone, ct);
            if (resultado.Sucesso)
            {
                TempData["Sucesso"] = "Dados atualizados.";
                return RedirectToAction(nameof(Index));
            }
            TempData["Erro"] = resultado.Erro;
        }
        else
        {
            TempData["Erro"] = "Confira os dados informados.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("alterar-senha")]
    public async Task<IActionResult> AlterarSenha(AlterarSenhaViewModel modelo, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Erro"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        var resultado = await _contas.AlterarSenhaAsync(UsuarioId, modelo.SenhaAtual, modelo.NovaSenha, ct);
        if (!resultado.Sucesso)
        {
            TempData["Erro"] = resultado.Erro;
            return RedirectToAction(nameof(Index));
        }
        // O carimbo mudou: sai e pede o login de novo com a nova senha
        await ServicoSessao.SairAsync(HttpContext);
        TempData["Sucesso"] = "Senha alterada. Entre novamente.";
        return RedirectToAction("Entrar", "Conta");
    }

    // ================================================================== endereços (RF07)

    [HttpGet("enderecos")]
    public async Task<IActionResult> Enderecos(CancellationToken ct) => View(await _enderecos.ListarAsync(UsuarioId, ct));

    [HttpGet("enderecos/novo")]
    public IActionResult NovoEndereco(string? retornarPara)
    {
        var nome = User.Identity?.Name ?? string.Empty;
        return View("EditarEndereco", new EnderecoViewModel { Destinatario = nome, RetornarPara = retornarPara });
    }

    [HttpGet("enderecos/{id:int}")]
    public async Task<IActionResult> EditarEndereco(int id, string? retornarPara, CancellationToken ct)
    {
        var e = await _enderecos.ObterAsync(UsuarioId, id, ct);
        if (e is null) return NotFound();
        var modelo = EnderecoViewModel.De(e);
        modelo.RetornarPara = retornarPara;
        return View(modelo);
    }

    [HttpPost("enderecos/salvar")]
    public async Task<IActionResult> SalvarEndereco(EnderecoViewModel modelo, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("EditarEndereco", modelo);

        var resultado = await _enderecos.SalvarAsync(UsuarioId, modelo.ParaEntidade(), ct);
        if (!resultado.Sucesso)
        {
            ModelState.AddModelError(string.Empty, resultado.Erro!);
            return View("EditarEndereco", modelo);
        }

        TempData["Sucesso"] = "Endereço salvo.";
        if (!string.IsNullOrEmpty(modelo.RetornarPara) && Url.IsLocalUrl(modelo.RetornarPara))
        {
            var separador = modelo.RetornarPara.Contains('?') ? '&' : '?';
            return LocalRedirect($"{modelo.RetornarPara}{separador}enderecoId={resultado.Valor}");
        }
        return RedirectToAction(nameof(Enderecos));
    }

    [HttpPost("enderecos/{id:int}/padrao")]
    public async Task<IActionResult> DefinirPadrao(int id, CancellationToken ct)
    {
        var r = await _enderecos.DefinirPadraoAsync(UsuarioId, id, ct);
        TempData[r.Sucesso ? "Sucesso" : "Erro"] = r.Sucesso ? "Endereço padrão alterado." : r.Erro;
        return RedirectToAction(nameof(Enderecos));
    }

    [HttpPost("enderecos/{id:int}/excluir")]
    public async Task<IActionResult> ExcluirEndereco(int id, CancellationToken ct)
    {
        var r = await _enderecos.ExcluirAsync(UsuarioId, id, ct);
        TempData[r.Sucesso ? "Sucesso" : "Erro"] = r.Sucesso ? "Endereço excluído." : r.Erro;
        return RedirectToAction(nameof(Enderecos));
    }

    // ================================================================== LGPD (RNF07)

    [HttpGet("privacidade")]
    public IActionResult Privacidade() => View();

    [HttpPost("privacidade/exportar")]
    public async Task<IActionResult> ExportarDados(CancellationToken ct)
    {
        var dados = await _contas.ExportarDadosAsync(UsuarioId, ct);
        if (dados is null) return NotFound();
        var json = JsonSerializer.SerializeToUtf8Bytes(dados, new JsonSerializerOptions { WriteIndented = true });
        return File(json, "application/json", $"meus-dados-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    [HttpPost("privacidade/excluir-conta")]
    public async Task<IActionResult> ExcluirConta(string senha, bool confirmo, CancellationToken ct)
    {
        if (!confirmo)
        {
            TempData["Erro"] = "Marque a confirmação para excluir a conta.";
            return RedirectToAction(nameof(Privacidade));
        }
        var resultado = await _contas.AnonimizarAsync(UsuarioId, senha, ct);
        if (!resultado.Sucesso)
        {
            TempData["Erro"] = resultado.Erro;
            return RedirectToAction(nameof(Privacidade));
        }
        await ServicoSessao.SairAsync(HttpContext);
        TempData["Sucesso"] = "Sua conta foi excluída e seus dados pessoais foram removidos.";
        return Redirect("/");
    }
}
