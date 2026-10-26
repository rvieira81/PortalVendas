using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PortalVendas.Application.WhatsApp;
using PortalVendas.Infrastructure.WhatsApp;

namespace PortalVendas.Web.Controllers;

/// <summary>
/// Mensagens recebidas no WhatsApp. Responde rápido: grava, põe na fila e o robô responde em segundo plano.
/// Meta: assinatura HMAC (App Secret). Evolution: token secreto no endereço do webhook.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("webhooks/whatsapp")]
public class WebhooksWhatsAppController : ControllerBase
{
    private const int TamanhoMaximoCorpo = 512 * 1024;

    private readonly ConfiguracaoWhatsAppService _configuracao;
    private readonly ProcessadorWhatsAppService _processador;
    private readonly IFilaWhatsApp _fila;
    private readonly ILogger<WebhooksWhatsAppController> _logger;

    public WebhooksWhatsAppController(ConfiguracaoWhatsAppService configuracao, ProcessadorWhatsAppService processador, IFilaWhatsApp fila,
        ILogger<WebhooksWhatsAppController> logger)
    {
        _configuracao = configuracao;
        _processador = processador;
        _fila = fila;
        _logger = logger;
    }

    /// <summary>Verificação do webhook no painel da Meta (hub.challenge).</summary>
    [HttpGet("meta")]
    [EnableRateLimiting("webhook")]
    public async Task<IActionResult> VerificarMeta([FromQuery(Name = "hub.mode")] string? modo, [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? desafio, CancellationToken ct)
    {
        if (!_configuracao.Licenciado) return NotFound();
        var config = await _configuracao.ObterAsync(ct);
        if (modo == "subscribe" && Iguais(token, config.MetaVerifyToken) && !string.IsNullOrEmpty(desafio) && desafio.Length <= 100)
            return Content(desafio, "text/plain");
        _logger.LogWarning("Verificação do webhook da Meta recusada (token não confere)");
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost("meta")]
    [EnableRateLimiting("whatsapp")]
    [RequestSizeLimit(TamanhoMaximoCorpo)]
    public async Task<IActionResult> Meta(CancellationToken ct)
    {
        if (!_configuracao.Licenciado) return NotFound();
        var corpo = await LerCorpoAsync(ct);
        var config = await _configuracao.ObterAsync(ct);
        if (!LeitorWebhook.AssinaturaMetaValida(corpo, Request.Headers["X-Hub-Signature-256"].ToString(), config.MetaAppSecret))
        {
            _logger.LogWarning("Webhook da Meta com assinatura inválida (IP {Ip})", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }
        return await ReceberAsync(corpo, LeitorWebhook.LerMeta, ct);
    }

    [HttpPost("evolution/{token}")]
    [EnableRateLimiting("whatsapp")]
    [RequestSizeLimit(TamanhoMaximoCorpo)]
    public async Task<IActionResult> Evolution(string token, CancellationToken ct)
    {
        if (!_configuracao.Licenciado) return NotFound();
        var config = await _configuracao.ObterAsync(ct);
        if (!Iguais(token, config.EvolutionTokenWebhook))
        {
            _logger.LogWarning("Webhook da Evolution com token inválido (IP {Ip})", HttpContext.Connection.RemoteIpAddress);
            return NotFound();
        }
        return await ReceberAsync(await LerCorpoAsync(ct), LeitorWebhook.LerEvolution, ct);
    }

    private async Task<IActionResult> ReceberAsync(byte[] corpo, Func<JsonElement, IReadOnlyList<MensagemRecebida>> ler, CancellationToken ct)
    {
        IReadOnlyList<MensagemRecebida> mensagens;
        try
        {
            using var json = JsonDocument.Parse(corpo);
            mensagens = ler(json.RootElement);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        foreach (var m in mensagens)
        {
            try
            {
                var id = await _processador.RegistrarEntradaAsync(m, ct);
                if (id is not null) _fila.Enfileirar(id.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Respondemos 200 mesmo assim: o provedor reenviaria e geraria duplicidade
                _logger.LogError(ex, "Falha ao registrar mensagem {IdExterno} do WhatsApp", m.IdExterno);
            }
        }
        return Ok();
    }

    private async Task<byte[]> LerCorpoAsync(CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        await Request.Body.CopyToAsync(memoria, ct);
        return memoria.ToArray();
    }

    private static bool Iguais(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
