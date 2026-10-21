namespace PortalVendas.Web.Infra;

/// <summary>Cabeçalhos de segurança (OWASP A05). A CSP libera só o necessário para Bootstrap (CDN) e Mercado Pago.</summary>
public class CabecalhosSegurancaMiddleware
{
    private const string Csp =
        "default-src 'self'; " +
        "script-src 'self' https://cdn.jsdelivr.net https://sdk.mercadopago.com https://*.mercadopago.com https://*.mercadolibre.com https://*.mlstatic.com; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com https://*.mlstatic.com; " +
        "img-src 'self' data: https:; " +
        "font-src 'self' data: https://cdn.jsdelivr.net https://fonts.gstatic.com https://*.mlstatic.com; " +
        "connect-src 'self' https://*.mercadopago.com https://*.mercadolibre.com https://*.mercadolivre.com https://*.mlstatic.com; " +
        "frame-src https://*.mercadopago.com https://*.mercadolibre.com https://*.mercadolivre.com; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "object-src 'none'";

    private readonly RequestDelegate _proximo;

    public CabecalhosSegurancaMiddleware(RequestDelegate proximo) => _proximo = proximo;

    public Task InvokeAsync(HttpContext contexto)
    {
        contexto.Response.OnStarting(() =>
        {
            var h = contexto.Response.Headers;
            h["Content-Security-Policy"] = Csp;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(self)";
            h["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
            h.Remove("X-Powered-By");
            h.Remove("Server");

            // Páginas e respostas JSON de usuário logado não ficam em cache (CSS, JS e imagens continuam em cache)
            var tipo = contexto.Response.ContentType ?? string.Empty;
            var conteudoDinamico = tipo.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
                                   || tipo.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
            if (conteudoDinamico && contexto.User.Identity?.IsAuthenticated == true && !h.ContainsKey("Cache-Control"))
                h["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
        return _proximo(contexto);
    }
}
