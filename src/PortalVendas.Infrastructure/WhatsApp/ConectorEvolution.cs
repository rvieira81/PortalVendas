using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PortalVendas.Application.WhatsApp;

namespace PortalVendas.Infrastructure.WhatsApp;

/// <summary>
/// Evolution API v2 (não oficial; o número da loja é conectado lendo um QR Code). Não há botões confiáveis:
/// as opções vão numeradas no texto e o cliente responde com o número.
/// </summary>
public sealed class ConectorEvolution : IConectorWhatsApp
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _instancia;
    private readonly string _apiKey;

    public ConectorEvolution(HttpClient http, string url, string instancia, string apiKey)
    {
        _http = http;
        _url = url.TrimEnd('/');
        _instancia = instancia;
        _apiKey = apiKey;
    }

    public ProvedorWhatsApp Provedor => ProvedorWhatsApp.Evolution;

    public Task<ResultadoEnvio> EnviarAsync(string telefone, MensagemSaida m, CancellationToken ct = default)
    {
        var texto = m.TextoComOpcoes();
        if (m.ImagemUrl is not null)
        {
            var extensao = Path.GetExtension(new Uri(m.ImagemUrl).AbsolutePath).ToLowerInvariant();
            var mime = extensao switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
            return PostarAsync("sendMedia", new JsonObject
            {
                ["number"] = telefone,
                ["mediatype"] = "image",
                ["mimetype"] = mime,
                ["caption"] = texto,
                ["media"] = m.ImagemUrl,
                ["fileName"] = "produto" + (extensao.Length is > 1 and < 6 ? extensao : ".jpg")
            }, ct);
        }
        return PostarAsync("sendText", new JsonObject { ["number"] = telefone, ["text"] = texto }, ct);
    }

    /// <summary>Sem a regra das 24 h: manda o texto normal.</summary>
    public Task<ResultadoEnvio> EnviarModeloAsync(string telefone, string modelo, string idioma, IReadOnlyList<string> parametros,
        string textoAlternativo, CancellationToken ct = default) =>
        EnviarAsync(telefone, MensagemSaida.Simples(textoAlternativo), ct);

    private async Task<ResultadoEnvio> PostarAsync(string acao, JsonObject corpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_url}/message/{acao}/{Uri.EscapeDataString(_instancia)}")
        {
            Content = JsonContent.Create(corpo)
        };
        req.Headers.Add("apikey", _apiKey);
        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var texto = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return ResultadoEnvio.Falha($"HTTP {(int)resp.StatusCode}: {ConectorMeta.Cortar(texto, 300)}");
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(texto) ? "{}" : texto);
            var id = json.RootElement.TryGetProperty("key", out var k) && k.TryGetProperty("id", out var i) ? i.GetString() : null;
            return ResultadoEnvio.Ok(id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ResultadoEnvio.Falha("Sem resposta da Evolution API: " + ex.Message);
        }
    }
}
