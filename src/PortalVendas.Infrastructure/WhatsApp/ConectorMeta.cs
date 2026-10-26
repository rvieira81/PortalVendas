using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PortalVendas.Application.WhatsApp;

namespace PortalVendas.Infrastructure.WhatsApp;

/// <summary>
/// WhatsApp Cloud API (oficial). Até 3 opções curtas viram botões; até 10, uma lista; acima disso, texto numerado.
/// Documentação: https://developers.facebook.com/docs/whatsapp/cloud-api/messages
/// </summary>
public sealed class ConectorMeta : IConectorWhatsApp
{
    public const string UrlGraph = "https://graph.facebook.com/v21.0/";

    private readonly HttpClient _http;
    private readonly string _phoneNumberId;
    private readonly string _token;

    public ConectorMeta(HttpClient http, string phoneNumberId, string token)
    {
        _http = http;
        _phoneNumberId = phoneNumberId;
        _token = token;
    }

    public ProvedorWhatsApp Provedor => ProvedorWhatsApp.Meta;

    public async Task<ResultadoEnvio> EnviarAsync(string telefone, MensagemSaida m, CancellationToken ct = default)
    {
        var opcoes = m.Opcoes ?? Array.Empty<OpcaoMenu>();

        if (opcoes.Count == 0)
        {
            if (m.ImagemUrl is null) return await PostarAsync(Texto(telefone, m.Texto), ct);
            if (m.Texto.Length <= 1024) return await PostarAsync(Imagem(telefone, m.ImagemUrl, m.Texto), ct);
            var foto = await PostarAsync(Imagem(telefone, m.ImagemUrl, null), ct);
            return foto.Sucesso ? await PostarAsync(Texto(telefone, m.Texto), ct) : foto;
        }

        if (m.Texto.Length > 1024 || opcoes.Count > 10)
        {
            if (m.ImagemUrl is not null) await PostarAsync(Imagem(telefone, m.ImagemUrl, null), ct);
            return await PostarAsync(Texto(telefone, m.TextoComOpcoes()), ct);
        }

        if (opcoes.Count <= 3 && opcoes.All(o => o.Titulo.Length <= 20))
            return await PostarAsync(Botoes(telefone, m), ct);

        if (m.ImagemUrl is not null)
        {
            var foto = await PostarAsync(Imagem(telefone, m.ImagemUrl, null), ct);
            if (!foto.Sucesso) return foto;
        }
        return await PostarAsync(Lista(telefone, m), ct);
    }

    public Task<ResultadoEnvio> EnviarModeloAsync(string telefone, string modelo, string idioma, IReadOnlyList<string> parametros,
        string textoAlternativo, CancellationToken ct = default)
    {
        var corpo = Base(telefone, "template");
        var modeloJson = new JsonObject
        {
            ["name"] = modelo,
            ["language"] = new JsonObject { ["code"] = idioma }
        };
        if (parametros.Count > 0)
        {
            modeloJson["components"] = new JsonArray(new JsonObject
            {
                ["type"] = "body",
                ["parameters"] = new JsonArray(parametros.Select(p => (JsonNode)new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = string.IsNullOrWhiteSpace(p) ? "-" : Cortar(p, 200)
                }).ToArray())
            });
        }
        corpo["template"] = modeloJson;
        return PostarAsync(corpo, ct);
    }

    // ------------------------------------------------------------------ montagem

    private static JsonObject Base(string telefone, string tipo) => new()
    {
        ["messaging_product"] = "whatsapp",
        ["recipient_type"] = "individual",
        ["to"] = telefone,
        ["type"] = tipo
    };

    private static JsonObject Texto(string telefone, string texto)
    {
        var o = Base(telefone, "text");
        o["text"] = new JsonObject { ["preview_url"] = true, ["body"] = Cortar(texto, 4096) };
        return o;
    }

    private static JsonObject Imagem(string telefone, string url, string? legenda)
    {
        var o = Base(telefone, "image");
        var imagem = new JsonObject { ["link"] = url };
        if (!string.IsNullOrEmpty(legenda)) imagem["caption"] = Cortar(legenda, 1024);
        o["image"] = imagem;
        return o;
    }

    private static JsonObject Botoes(string telefone, MensagemSaida m)
    {
        var interativo = new JsonObject
        {
            ["type"] = "button",
            ["body"] = new JsonObject { ["text"] = m.Texto },
            ["action"] = new JsonObject
            {
                ["buttons"] = new JsonArray(m.Opcoes!.Select(op => (JsonNode)new JsonObject
                {
                    ["type"] = "reply",
                    ["reply"] = new JsonObject { ["id"] = Cortar(op.Id, 256), ["title"] = Cortar(op.Titulo, 20) }
                }).ToArray())
            }
        };
        if (m.ImagemUrl is not null)
            interativo["header"] = new JsonObject { ["type"] = "image", ["image"] = new JsonObject { ["link"] = m.ImagemUrl } };
        var o = Base(telefone, "interactive");
        o["interactive"] = interativo;
        return o;
    }

    private static JsonObject Lista(string telefone, MensagemSaida m)
    {
        var linhas = m.Opcoes!.Select(op =>
        {
            var linha = new JsonObject { ["id"] = Cortar(op.Id, 200), ["title"] = Cortar(op.Titulo, 24) };
            // Título cortado: o nome completo vai para a descrição
            var descricao = op.Titulo.Length > 24 ? op.Titulo + (string.IsNullOrEmpty(op.Descricao) ? "" : " — " + op.Descricao) : op.Descricao;
            if (!string.IsNullOrEmpty(descricao)) linha["description"] = Cortar(descricao, 72);
            return (JsonNode)linha;
        }).ToArray();

        var o = Base(telefone, "interactive");
        o["interactive"] = new JsonObject
        {
            ["type"] = "list",
            ["body"] = new JsonObject { ["text"] = m.Texto },
            ["action"] = new JsonObject
            {
                ["button"] = Cortar(m.TituloLista ?? "Ver opções", 20),
                ["sections"] = new JsonArray(new JsonObject
                {
                    ["title"] = Cortar(m.TituloLista ?? "Opções", 24),
                    ["rows"] = new JsonArray(linhas)
                })
            }
        };
        return o;
    }

    private async Task<ResultadoEnvio> PostarAsync(JsonObject corpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_phoneNumberId}/messages") { Content = JsonContent.Create(corpo) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var texto = await resp.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(texto) ? "{}" : texto);
            if (resp.IsSuccessStatusCode)
            {
                var id = json.RootElement.TryGetProperty("messages", out var ms) && ms.GetArrayLength() > 0
                         && ms[0].TryGetProperty("id", out var i) ? i.GetString() : null;
                return ResultadoEnvio.Ok(id);
            }
            var erro = json.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var msg)
                ? $"{(int)resp.StatusCode}: {msg.GetString()}"
                : $"HTTP {(int)resp.StatusCode}";
            return ResultadoEnvio.Falha(erro);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ResultadoEnvio.Falha("Sem resposta da Meta: " + ex.Message);
        }
    }

    internal static string Cortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";
}
