using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortalVendas.Application.Frete;

namespace PortalVendas.Infrastructure.Frete;

public class MelhorEnvioOptions
{
    public const string Secao = "MelhorEnvio";

    /// <summary>Produção: https://melhorenvio.com.br/ — Homologação: https://sandbox.melhorenvio.com.br/</summary>
    public string UrlBase { get; set; } = "https://melhorenvio.com.br/";
    /// <summary>Token de acesso (Painel → Integrações → Permissões de acesso → Gerar token). Segredo.</summary>
    public string Token { get; set; } = string.Empty;
    /// <summary>E-mail de contato técnico: vai no User-Agent, que o Melhor Envio exige.</summary>
    public string EmailContato { get; set; } = string.Empty;
    /// <summary>Ids dos serviços separados por vírgula (vazio = todos os habilitados na conta). Ex.: "1,2,3,4,17".</summary>
    public string? Servicos { get; set; }
    public bool AvisoRecebimento { get; set; }
    public bool MaoPropria { get; set; }
    public int TimeoutSegundos { get; set; } = 8;
    public int CacheMinutos { get; set; } = 10;
}

/// <summary>Cotação de frete por peso, dimensões e valor declarado no Melhor Envio (ADR-006).</summary>
public class MelhorEnvioProvider : IProvedorFrete
{
    private readonly HttpClient _http;
    private readonly MelhorEnvioOptions _opcoes;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MelhorEnvioProvider> _logger;

    public MelhorEnvioProvider(HttpClient http, IOptions<MelhorEnvioOptions> opcoes, IMemoryCache cache, ILogger<MelhorEnvioProvider> logger)
    {
        _http = http;
        _opcoes = opcoes.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CotacaoProvedor>> CotarAsync(SolicitacaoFrete s, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.Token))
            throw new InvalidOperationException("Melhor Envio não configurado (MelhorEnvio:Token).");

        var chave = ChaveCache(s);
        if (_cache.TryGetValue(chave, out IReadOnlyList<CotacaoProvedor>? emCache) && emCache is not null)
            return emCache;

        var corpo = MontarCorpo(s, _opcoes);
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v2/me/shipment/calculate")
        {
            Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opcoes.Token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("User-Agent", $"PortalVendas ({_opcoes.EmailContato})");

        using var resp = await _http.SendAsync(req, ct);
        var conteudo = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Melhor Envio respondeu {Status}: {Corpo}", (int)resp.StatusCode, conteudo.Length > 1000 ? conteudo[..1000] : conteudo);
            throw new HttpRequestException($"Melhor Envio respondeu {(int)resp.StatusCode}.");
        }

        var cotacoes = Ler(conteudo);
        _cache.Set(chave, cotacoes, TimeSpan.FromMinutes(Math.Max(1, _opcoes.CacheMinutos)));
        return cotacoes;
    }

    internal static JsonObject MontarCorpo(SolicitacaoFrete s, MelhorEnvioOptions opcoes)
    {
        var produtos = new JsonArray();
        foreach (var i in s.Itens)
        {
            produtos.Add(new JsonObject
            {
                ["id"] = i.ProdutoId.ToString(CultureInfo.InvariantCulture),
                ["width"] = i.LarguraCm,
                ["height"] = i.AlturaCm,
                ["length"] = i.ComprimentoCm,
                ["weight"] = i.PesoKg,
                ["insurance_value"] = Math.Round(i.ValorUnitario, 2),
                ["quantity"] = i.Quantidade
            });
        }

        var corpo = new JsonObject
        {
            ["from"] = new JsonObject { ["postal_code"] = s.CepOrigem },
            ["to"] = new JsonObject { ["postal_code"] = s.CepDestino },
            ["products"] = produtos,
            ["options"] = new JsonObject { ["receipt"] = opcoes.AvisoRecebimento, ["own_hand"] = opcoes.MaoPropria }
        };
        if (!string.IsNullOrWhiteSpace(opcoes.Servicos)) corpo["services"] = opcoes.Servicos.Replace(" ", "");
        return corpo;
    }

    /// <summary>Lê a resposta: usa custom_price/custom_delivery_time (com as regras da conta) e ignora opções com erro.</summary>
    internal static IReadOnlyList<CotacaoProvedor> Ler(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<CotacaoProvedor>();

        var lista = new List<CotacaoProvedor>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (e.TryGetProperty("error", out var erro) && erro.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) continue;
            if (!e.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;

            var valor = Decimal(e, "custom_price") ?? Decimal(e, "price");
            var prazo = Inteiro(e, "custom_delivery_time") ?? Inteiro(e, "delivery_time");
            if (valor is null || prazo is null) continue;

            var servico = e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : $"Serviço {id}";
            var empresa = e.TryGetProperty("company", out var c) && c.ValueKind == JsonValueKind.Object
                          && c.TryGetProperty("name", out var cn) && cn.ValueKind == JsonValueKind.String
                ? cn.GetString()!
                : "Transportadora";

            lista.Add(new CotacaoProvedor(id, empresa, servico, valor.Value, prazo.Value));
        }
        return lista;
    }

    private static decimal? Decimal(JsonElement e, string nome)
    {
        if (!e.TryGetProperty(nome, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    private static int? Inteiro(JsonElement e, string nome)
    {
        if (!e.TryGetProperty(nome, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };
    }

    private static string ChaveCache(SolicitacaoFrete s)
    {
        var sb = new StringBuilder("frete:").Append(s.CepOrigem).Append(':').Append(s.CepDestino);
        foreach (var i in s.Itens.OrderBy(x => x.ProdutoId))
            sb.Append(':').Append(i.ProdutoId).Append('x').Append(i.Quantidade).Append('@').Append(i.ValorUnitario.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
