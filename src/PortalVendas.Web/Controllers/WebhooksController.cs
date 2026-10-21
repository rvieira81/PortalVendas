using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PortalVendas.Application.Pagamentos;
using PortalVendas.Infrastructure.Pagamentos;

namespace PortalVendas.Web.Controllers;

/// <summary>RF13: notificações do Mercado Pago. Sem antiforgery (é servidor-servidor); a autenticidade vem da assinatura HMAC.</summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("webhooks")]
public class WebhooksController : ControllerBase
{
    private const int TamanhoMaximoCorpo = 64 * 1024;

    private readonly PagamentoService _pagamentos;
    private readonly MercadoPagoOptions _opcoes;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(PagamentoService pagamentos, IOptions<MercadoPagoOptions> opcoes, ILogger<WebhooksController> logger)
    {
        _pagamentos = pagamentos;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    [HttpPost("mercadopago")]
    [EnableRateLimiting("webhook")]
    [RequestSizeLimit(TamanhoMaximoCorpo)]
    public async Task<IActionResult> MercadoPago(CancellationToken ct)
    {
        string corpo;
        using (var leitor = new StreamReader(Request.Body))
            corpo = await leitor.ReadToEndAsync(ct);

        // O id vem na query (data.id) — é ele que entra na assinatura. Corpo e formato antigo (?id=&topic=) como alternativa.
        string? dataId = Request.Query["data.id"];
        string? topico = Request.Query["type"];
        if (string.IsNullOrEmpty(topico)) topico = Request.Query["topic"];
        if (string.IsNullOrEmpty(dataId)) dataId = Request.Query["id"];

        if (string.IsNullOrEmpty(dataId) || string.IsNullOrEmpty(topico))
        {
            try
            {
                using var doc = JsonDocument.Parse(corpo);
                var raiz = doc.RootElement;
                if (string.IsNullOrEmpty(topico) && raiz.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                    topico = t.GetString();
                if (string.IsNullOrEmpty(dataId) && raiz.TryGetProperty("data", out var d) && d.TryGetProperty("id", out var i))
                    dataId = i.ValueKind == JsonValueKind.String ? i.GetString() : i.GetRawText();
            }
            catch (JsonException)
            {
                return BadRequest();
            }
        }

        if (string.IsNullOrEmpty(dataId) || string.IsNullOrEmpty(topico) || dataId.Length > 50 || topico.Length > 50)
            return BadRequest();

        var requestId = Request.Headers["x-request-id"].ToString();
        var assinaturaValida = AssinaturaWebhook.Validar(Request.Headers["x-signature"].ToString(), requestId, dataId, _opcoes.SegredoWebhook);
        if (!assinaturaValida)
            _logger.LogWarning("Webhook do Mercado Pago com assinatura inválida (recurso {RecursoId}, IP {Ip})",
                dataId, HttpContext.Connection.RemoteIpAddress);

        try
        {
            await _pagamentos.ProcessarNotificacaoAsync(
                new NotificacaoRecebida(string.IsNullOrEmpty(requestId) ? null : requestId, topico, dataId, corpo, assinaturaValida), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Registrado; o job de conciliação recupera. Respondemos 200 para o Mercado Pago não insistir indefinidamente.
            _logger.LogError(ex, "Erro ao processar webhook {RequestId}", requestId);
        }

        return assinaturaValida ? Ok() : Unauthorized();
    }
}
