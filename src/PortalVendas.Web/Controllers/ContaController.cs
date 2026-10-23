using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Contas;
using PortalVendas.Web.Infra;
using PortalVendas.Web.Models;

namespace PortalVendas.Web.Controllers;

[Route("conta")]
public class ContaController : Controller
{
    private readonly ContaService _contas;
    private readonly ServicoSessao _sessao;

    public ContaController(ContaService contas, ServicoSessao sessao)
    {
        _contas = contas;
        _sessao = sessao;
    }

    // ================================================================== entrar / sair

    [HttpGet("entrar")]
    public IActionResult Entrar(string? returnUrl)
    {
        if (User.UsuarioId() is not null) return RedirecionarSeguro(returnUrl);
        return View(new EntrarViewModel { RetornarPara = returnUrl });
    }

    [HttpPost("entrar")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Entrar(EntrarViewModel modelo, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(modelo);

        var resultado = await _contas.AutenticarAsync(modelo.Email, modelo.Senha, ct);
        switch (resultado.Situacao)
        {
            case SituacaoLogin.Sucesso:
                await _sessao.EntrarAsync(HttpContext, resultado.Usuario!, comMfa: false, modelo.Lembrar);
                return RedirecionarSeguro(modelo.RetornarPara);

            case SituacaoLogin.RequerCodigoTotp:
                await _sessao.IniciarSegundoFatorAsync(HttpContext, resultado.Usuario!);
                return RedirectToAction(nameof(Codigo));

            case SituacaoLogin.RequerConfiguracaoTotp:
                // Primeiro acesso do administrador: entra só com permissão para configurar o autenticador
                await _sessao.EntrarAsync(HttpContext, resultado.Usuario!, comMfa: false, lembrar: false);
                return RedirectToAction("Configurar", "Seguranca", new { area = "Admin" });

            case SituacaoLogin.Bloqueado:
                ModelState.AddModelError(string.Empty, MensagemBloqueio(resultado.BloqueadoAte));
                return View(modelo);

            default:
                // Mensagem única: não diz se o e-mail existe
                ModelState.AddModelError(string.Empty, "E-mail ou senha incorretos.");
                return View(modelo);
        }
    }

    [HttpGet("codigo")]
    public async Task<IActionResult> Codigo()
    {
        if (await ServicoSessao.ObterUsuarioSegundoFatorAsync(HttpContext) is null) return RedirectToAction(nameof(Entrar));
        return View(new CodigoViewModel());
    }

    [HttpPost("codigo")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Codigo(CodigoViewModel modelo, CancellationToken ct)
    {
        var usuarioId = await ServicoSessao.ObterUsuarioSegundoFatorAsync(HttpContext);
        if (usuarioId is null) return RedirectToAction(nameof(Entrar));
        if (!ModelState.IsValid) return View(modelo);

        var resultado = await _contas.VerificarTotpAsync(usuarioId.Value, modelo.Codigo, ct);
        if (resultado.Situacao == SituacaoLogin.Sucesso)
        {
            await _sessao.EntrarAsync(HttpContext, resultado.Usuario!, comMfa: true, lembrar: false);
            return RedirectToAction("Index", "Painel", new { area = "Admin" });
        }

        ModelState.AddModelError(string.Empty, resultado.Situacao == SituacaoLogin.Bloqueado
            ? MensagemBloqueio(resultado.BloqueadoAte)
            : "Código incorreto.");
        return View(modelo);
    }

    [HttpPost("sair")]
    public async Task<IActionResult> Sair()
    {
        await ServicoSessao.SairAsync(HttpContext);
        return Redirect("/");
    }

    /// <summary>
    /// Link enviado pelo robô do WhatsApp: entra na conta do cliente (criada na conversa, sem senha)
    /// e abre o pagamento do pedido para pagar com cartão ou boleto.
    /// </summary>
    [HttpGet("acesso-whatsapp")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> AcessoWhatsApp(string? token, int? pedido, CancellationToken ct)
    {
        var usuario = await _contas.ValidarAcessoWhatsAppAsync(token, ct);
        if (usuario is null)
        {
            TempData["Erro"] = "Este link expirou. Peça um novo no WhatsApp (escreva \"meus pedidos\") ou entre com sua conta.";
            return RedirectToAction(nameof(Entrar));
        }
        if (User.UsuarioId() is { } atual && atual != usuario.Id) await ServicoSessao.SairAsync(HttpContext);
        await _sessao.EntrarAsync(HttpContext, usuario, comMfa: false, lembrar: false);
        return pedido is > 0 ? Redirect($"/pedidos/{pedido}/pagamento") : Redirect("/pedidos");
    }

    [HttpGet("acesso-negado")]
    public IActionResult AcessoNegado()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("Erro", new ErroViewModel { Codigo = 403, RequestId = HttpContext.TraceIdentifier });
    }

    // ================================================================== cadastro

    [HttpGet("cadastrar")]
    public IActionResult Cadastrar(string? returnUrl)
    {
        if (User.UsuarioId() is not null) return Redirect("/");
        return View(new CadastroViewModel { RetornarPara = returnUrl });
    }

    [HttpPost("cadastrar")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Cadastrar(CadastroViewModel modelo, CancellationToken ct)
    {
        if (!modelo.AceitouTermos)
            ModelState.AddModelError(nameof(modelo.AceitouTermos), "É preciso aceitar os termos para criar a conta.");
        if (!ModelState.IsValid) return View(modelo);

        var resultado = await _contas.CadastrarAsync(
            new ComandoCadastro(modelo.Nome, modelo.Email, modelo.Cpf, modelo.Telefone, modelo.Senha, modelo.AceitouTermos), ct);

        if (!resultado.Sucesso)
        {
            ModelState.AddModelError(string.Empty, resultado.Erro!);
            return View(modelo);
        }

        // Resposta igual com ou sem conta existente (não revela e-mails cadastrados)
        return View("CadastroEnviado", ContaService.NormalizarEmail(modelo.Email));
    }

    [HttpGet("confirmar-email")]
    public async Task<IActionResult> ConfirmarEmail(string? token, CancellationToken ct)
    {
        var resultado = await _contas.ConfirmarEmailAsync(token ?? string.Empty, ct);
        return View(resultado);
    }

    [Authorize]
    [HttpPost("reenviar-confirmacao")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ReenviarConfirmacao(CancellationToken ct)
    {
        await _contas.ReenviarConfirmacaoAsync(User.UsuarioIdObrigatorio(), ct);
        TempData["Sucesso"] = "Se o e-mail ainda não foi confirmado, enviamos um novo link. Confira também o spam.";
        return RedirectToAction("Index", "MinhaConta");
    }

    // ================================================================== senha (RF06)

    [HttpGet("esqueci-senha")]
    public IActionResult EsqueciSenha() => View(new EsqueciSenhaViewModel());

    [HttpPost("esqueci-senha")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> EsqueciSenha(EsqueciSenhaViewModel modelo, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(modelo);
        await _contas.SolicitarRedefinicaoSenhaAsync(modelo.Email, ct);
        TempData["Sucesso"] = "Se houver uma conta com este e-mail, você vai receber um link para criar uma nova senha em instantes.";
        return RedirectToAction(nameof(Entrar));
    }

    [HttpGet("redefinir-senha")]
    public async Task<IActionResult> RedefinirSenha(string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || !await _contas.TokenRedefinicaoValidoAsync(token, ct))
        {
            TempData["Erro"] = "Link inválido ou expirado. Peça uma nova redefinição.";
            return RedirectToAction(nameof(EsqueciSenha));
        }
        return View(new RedefinirSenhaViewModel { Token = token });
    }

    [HttpPost("redefinir-senha")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> RedefinirSenha(RedefinirSenhaViewModel modelo, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(modelo);
        var resultado = await _contas.RedefinirSenhaAsync(modelo.Token, modelo.Senha, ct);
        if (!resultado.Sucesso)
        {
            ModelState.AddModelError(string.Empty, resultado.Erro!);
            return View(modelo);
        }
        await ServicoSessao.SairAsync(HttpContext);
        TempData["Sucesso"] = "Senha alterada. Entre com a nova senha.";
        return RedirectToAction(nameof(Entrar));
    }

    // ================================================================== auxiliares

    private IActionResult RedirecionarSeguro(string? url) =>
        !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? LocalRedirect(url) : Redirect("/");

    private static string MensagemBloqueio(DateTime? ate)
    {
        var quando = ate is null ? "por alguns minutos" : $"até as {Horario.ParaBrasilia(ate.Value):HH:mm}";
        return $"Muitas tentativas incorretas. Por segurança, o acesso foi bloqueado {quando}. Se esqueceu a senha, use \"Esqueci minha senha\".";
    }
}
