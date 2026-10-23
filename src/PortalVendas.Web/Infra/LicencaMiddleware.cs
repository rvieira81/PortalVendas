using System.Net;
using PortalVendas.Application.Licenciamento;

namespace PortalVendas.Web.Infra;

/// <summary>
/// A loja só atende nos domínios da licença. Fora deles (ou sem licença válida) a vitrine mostra o aviso
/// "loja não licenciada". Continuam acessíveis: o health check, os arquivos estáticos, o login, o painel
/// (para instalar a licença) e o webhook (pagamentos que já estavam em andamento).
/// Em localhost, acessado da própria máquina, tudo funciona — sem licença, como demonstração.
/// </summary>
public sealed class LicencaMiddleware
{
    private static readonly string[] SempreLiberados = { "/health", "/conta", "/admin", "/webhooks", "/erro", "/css", "/js", "/img" };

    private readonly RequestDelegate _proximo;
    private readonly ILicencaLoja _licenca;
    private readonly ILogger<LicencaMiddleware> _logger;
    private DateTime _ultimoAviso = DateTime.MinValue;

    public LicencaMiddleware(RequestDelegate proximo, ILicencaLoja licenca, ILogger<LicencaMiddleware> logger)
    {
        _proximo = proximo;
        _licenca = licenca;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        var remoto = contexto.Connection.RemoteIpAddress;
        var local = remoto is null || IPAddress.IsLoopback(remoto);
        var caminho = contexto.Request.Path;

        if (_licenca.HostAutorizado(contexto.Request.Host.Host, local)
            || SempreLiberados.Any(p => caminho.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _proximo(contexto);
            return;
        }

        if (DateTime.UtcNow - _ultimoAviso > TimeSpan.FromMinutes(5))
        {
            _ultimoAviso = DateTime.UtcNow;
            _logger.LogWarning("Acesso recusado: {Host} não está na licença ({Situacao}: {Mensagem})",
                contexto.Request.Host.Host, _licenca.Estado.Situacao, _licenca.Estado.Mensagem);
        }

        contexto.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        contexto.Response.ContentType = "text/html; charset=utf-8";
        contexto.Response.Headers.CacheControl = "no-store";
        await contexto.Response.WriteAsync("""
            <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="robots" content="noindex"><title>Loja indisponível</title></head>
            <body style="margin:0;min-height:100vh;display:grid;place-items:center;background:#F4F7F6;font-family:Segoe UI,system-ui,sans-serif;color:#24363A">
            <main style="max-width:520px;margin:24px;background:#fff;border:1px solid #DCE5E2;border-radius:16px;padding:32px;text-align:center">
            <h1 style="margin:0 0 12px;font-size:24px;color:#12343B">Loja temporariamente indisponível</h1>
            <p style="margin:0 0 8px;line-height:1.5">Este endereço não está autorizado na licença do sistema.</p>
            <p style="margin:0;line-height:1.5;color:#5E7276;font-size:14px">Se você é o responsável pela loja, entre no painel administrativo e instale a licença enviada pelo fornecedor.</p>
            </main></body></html>
            """);
    }
}
