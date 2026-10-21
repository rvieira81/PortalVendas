using PortalVendas.Application.Carrinhos;

namespace PortalVendas.Web.Infra;

/// <summary>Identifica o carrinho da requisição: pela conta (logado) ou pelo cookie (anônimo).</summary>
public class ContextoCarrinho
{
    public const string NomeCookie = "pv_carrinho";
    private readonly IHttpContextAccessor _acessor;

    public ContextoCarrinho(IHttpContextAccessor acessor) => _acessor = acessor;

    private HttpContext Http => _acessor.HttpContext ?? throw new InvalidOperationException("Sem HttpContext.");

    public Guid? TokenAnonimo =>
        Guid.TryParse(Http.Request.Cookies[NomeCookie], out var token) ? token : null;

    public IdentificacaoCarrinho Identificacao
    {
        get
        {
            var usuarioId = Http.User.UsuarioId();
            return usuarioId is not null
                ? new IdentificacaoCarrinho(null, usuarioId)
                : new IdentificacaoCarrinho(TokenAnonimo, null);
        }
    }

    /// <summary>Grava o cookie do carrinho anônimo (30 dias).</summary>
    public void GravarToken(Guid token)
    {
        if (Http.User.UsuarioId() is not null || TokenAnonimo == token) return;
        Http.Response.Cookies.Append(NomeCookie, token.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
    }

    public void RemoverCookie() => Http.Response.Cookies.Delete(NomeCookie);
}
