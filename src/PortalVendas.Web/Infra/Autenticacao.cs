using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PortalVendas.Application.Carrinhos;
using PortalVendas.Application.Contas;
using PortalVendas.Domain.Usuarios;

namespace PortalVendas.Web.Infra;

public static class Esquemas
{
    public const string Principal = CookieAuthenticationDefaults.AuthenticationScheme;
    /// <summary>Cookie curto entre a senha e o código TOTP do administrador.</summary>
    public const string PreMfa = "PreMfa";
}

public static class Perfis
{
    public const string Cliente = "Cliente";
    public const string Administrador = "Administrador";
}

public static class Politicas
{
    public const string Admin = "Admin";
    public const string AdminConfigurarMfa = "AdminConfigurarMfa";
}

public static class Reivindicacoes
{
    public const string Carimbo = "pv:carimbo";
    public const string VerificadoEm = "pv:verificado";
    public const string MetodoAutenticacao = "amr";
    public const string Mfa = "mfa";
    public const string Senha = "pwd";
}

public static class ClaimsPrincipalExtensions
{
    public static int? UsuarioId(this ClaimsPrincipal usuario) =>
        usuario.Identity?.IsAuthenticated == true && int.TryParse(usuario.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static int UsuarioIdObrigatorio(this ClaimsPrincipal usuario) =>
        usuario.UsuarioId() ?? throw new InvalidOperationException("Usuário não autenticado.");

    public static bool EhAdministrador(this ClaimsPrincipal usuario) => usuario.IsInRole(Perfis.Administrador);

    public static string Autor(this ClaimsPrincipal usuario) =>
        usuario.FindFirstValue(ClaimTypes.Email) ?? $"usuario:{usuario.UsuarioId()}";
}

/// <summary>Cria e encerra sessões e mescla o carrinho anônimo no login.</summary>
public class ServicoSessao
{
    private readonly CarrinhoService _carrinhos;
    private readonly ContextoCarrinho _contextoCarrinho;

    public ServicoSessao(CarrinhoService carrinhos, ContextoCarrinho contextoCarrinho)
    {
        _carrinhos = carrinhos;
        _contextoCarrinho = contextoCarrinho;
    }

    public async Task EntrarAsync(HttpContext http, Usuario usuario, bool comMfa, bool lembrar)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Nome),
            new(ClaimTypes.Email, usuario.Email),
            new(ClaimTypes.Role, usuario.EhAdministrador ? Perfis.Administrador : Perfis.Cliente),
            new(Reivindicacoes.Carimbo, usuario.CarimboSeguranca.ToString()),
            new(Reivindicacoes.VerificadoEm, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            new(Reivindicacoes.MetodoAutenticacao, comMfa ? Reivindicacoes.Mfa : Reivindicacoes.Senha)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Esquemas.Principal));

        // Administrador nunca recebe sessão persistente
        var persistente = lembrar && !usuario.EhAdministrador;
        var propriedades = new AuthenticationProperties
        {
            IsPersistent = persistente,
            ExpiresUtc = persistente ? DateTimeOffset.UtcNow.AddHours(8) : null,
            AllowRefresh = true
        };

        await http.SignOutAsync(Esquemas.PreMfa);
        await http.SignInAsync(Esquemas.Principal, principal, propriedades);

        if (!usuario.EhAdministrador)
        {
            await _carrinhos.MesclarAoEntrarAsync(_contextoCarrinho.TokenAnonimo, usuario.Id);
            _contextoCarrinho.RemoverCookie();
        }
    }

    public async Task IniciarSegundoFatorAsync(HttpContext http, Usuario usuario)
    {
        var identidade = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()) }, Esquemas.PreMfa);
        await http.SignInAsync(Esquemas.PreMfa, new ClaimsPrincipal(identidade),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5) });
    }

    public static async Task<int?> ObterUsuarioSegundoFatorAsync(HttpContext http)
    {
        var resultado = await http.AuthenticateAsync(Esquemas.PreMfa);
        return resultado.Succeeded ? resultado.Principal?.UsuarioId() : null;
    }

    public static Task SairAsync(HttpContext http) => http.SignOutAsync(Esquemas.Principal);
}

/// <summary>
/// A cada 5 minutos confere no banco se o carimbo de segurança da sessão ainda é o do usuário
/// (troca de senha ou exclusão da conta derrubam as sessões abertas).
/// </summary>
public static class ValidadorSessao
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);

    public static async Task ValidarAsync(CookieValidatePrincipalContext contexto)
    {
        var principal = contexto.Principal;
        if (principal is null) return;

        var id = principal.UsuarioId();
        if (id is null || !Guid.TryParse(principal.FindFirstValue(Reivindicacoes.Carimbo), out var carimbo))
        {
            await RejeitarAsync(contexto);
            return;
        }

        if (long.TryParse(principal.FindFirstValue(Reivindicacoes.VerificadoEm), out var verificadoEm)
            && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(verificadoEm) < Intervalo)
            return;

        var contas = contexto.HttpContext.RequestServices.GetRequiredService<ContaService>();
        if (!await contas.SessaoValidaAsync(id.Value, carimbo, contexto.HttpContext.RequestAborted))
        {
            await RejeitarAsync(contexto);
            return;
        }

        var claims = principal.Claims.Where(c => c.Type != Reivindicacoes.VerificadoEm).ToList();
        claims.Add(new Claim(Reivindicacoes.VerificadoEm, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()));
        contexto.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, Esquemas.Principal)));
        contexto.ShouldRenew = true;
    }

    private static async Task RejeitarAsync(CookieValidatePrincipalContext contexto)
    {
        contexto.RejectPrincipal();
        await contexto.HttpContext.SignOutAsync(Esquemas.Principal);
    }
}
