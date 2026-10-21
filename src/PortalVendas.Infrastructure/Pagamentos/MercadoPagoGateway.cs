using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortalVendas.Application.Pagamentos;

namespace PortalVendas.Infrastructure.Pagamentos;

public class MercadoPagoOptions
{
    public const string Secao = "MercadoPago";

    public string UrlBase { get; set; } = "https://api.mercadopago.com/";
    /// <summary>Access Token (TEST-... em homologação, APP_USR-... em produção). Segredo: nunca no repositório.</summary>
    public string AccessToken { get; set; } = string.Empty;
    /// <summary>Public Key usada pelo Card Payment Brick no navegador (não é segredo).</summary>
    public string PublicKey { get; set; } = string.Empty;
    /// <summary>Chave secreta do webhook (Suas integrações → Webhooks). Segredo.</summary>
    public string SegredoWebhook { get; set; } = string.Empty;
    /// <summary>URL pública HTTPS do webhook, ex.: https://loja.com.br/webhooks/mercadopago</summary>
    public string? UrlNotificacao { get; set; }
    /// <summary>Nome na fatura do cartão (até 13 caracteres).</summary>
    public string? NomeNaFatura { get; set; }
    public int TimeoutSegundos { get; set; } = 20;
}

/// <summary>Cliente da API v1/payments do Mercado Pago (ADR-005).</summary>
public class MercadoPagoGateway : IGatewayPagamento
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    private readonly HttpClient _http;
    private readonly MercadoPagoOptions _opcoes;
    private readonly ILogger<MercadoPagoGateway> _logger;

    public MercadoPagoGateway(HttpClient http, IOptions<MercadoPagoOptions> opcoes, ILogger<MercadoPagoGateway> logger)
    {
        _http = http;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public Task<RespostaGateway> CriarPixAsync(SolicitacaoPix s, CancellationToken ct = default)
    {
        var corpo = CorpoBase(s.Valor, s.Descricao, s.PedidoId, "pix");
        corpo["date_of_expiration"] = FormatarData(s.Expiracao);
        corpo["payer"] = PagadorJson(s.Pagador, incluirEndereco: false);
        return CriarAsync(corpo, s.ChaveIdempotencia, ct);
    }

    public Task<RespostaGateway> CriarBoletoAsync(SolicitacaoBoleto s, CancellationToken ct = default)
    {
        var corpo = CorpoBase(s.Valor, s.Descricao, s.PedidoId, "bolbradesco");
        corpo["date_of_expiration"] = FormatarData(s.Vencimento);
        corpo["payer"] = PagadorJson(s.Pagador, incluirEndereco: true);
        return CriarAsync(corpo, s.ChaveIdempotencia, ct);
    }

    public Task<RespostaGateway> CriarCartaoAsync(SolicitacaoCartao s, CancellationToken ct = default)
    {
        var corpo = CorpoBase(s.Valor, s.Descricao, s.PedidoId, s.MetodoPagamentoId);
        corpo["token"] = s.Token;
        corpo["installments"] = s.Parcelas;
        if (!string.IsNullOrWhiteSpace(s.EmissorId)) corpo["issuer_id"] = s.EmissorId;
        if (!string.IsNullOrWhiteSpace(_opcoes.NomeNaFatura))
            corpo["statement_descriptor"] = _opcoes.NomeNaFatura.Length > 13 ? _opcoes.NomeNaFatura[..13] : _opcoes.NomeNaFatura;

        var payer = new JsonObject { ["email"] = s.EmailPagador };
        if (!string.IsNullOrWhiteSpace(s.NumeroDocumento))
            payer["identification"] = new JsonObject { ["type"] = s.TipoDocumento ?? "CPF", ["number"] = s.NumeroDocumento };
        corpo["payer"] = payer;
        return CriarAsync(corpo, s.ChaveIdempotencia, ct);
    }

    public async Task<RespostaGateway> ConsultarAsync(long gatewayId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/payments/{gatewayId}");
        using var doc = await EnviarAsync(req, ct);
        return Ler(doc.RootElement);
    }

    public async Task EstornarAsync(long gatewayId, Guid chaveIdempotencia, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"v1/payments/{gatewayId}/refunds")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        // Chave diferente da criação (operação diferente), mas estável para o mesmo pagamento
        req.Headers.Add("X-Idempotency-Key", $"estorno-{chaveIdempotencia:N}");
        using var _ = await EnviarAsync(req, ct);
        _logger.LogInformation("Estorno solicitado para o pagamento {GatewayId}", gatewayId);
    }

    // ------------------------------------------------------------------ montagem

    private JsonObject CorpoBase(decimal valor, string descricao, int pedidoId, string metodo)
    {
        var corpo = new JsonObject
        {
            ["transaction_amount"] = Math.Round(valor, 2),
            ["description"] = descricao,
            ["payment_method_id"] = metodo,
            ["external_reference"] = pedidoId.ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(_opcoes.UrlNotificacao))
            corpo["notification_url"] = _opcoes.UrlNotificacao;
        return corpo;
    }

    private static JsonObject PagadorJson(Pagador p, bool incluirEndereco)
    {
        var payer = new JsonObject
        {
            ["email"] = p.Email,
            ["first_name"] = p.Nome,
            ["last_name"] = p.Sobrenome,
            ["identification"] = new JsonObject { ["type"] = "CPF", ["number"] = p.Cpf }
        };
        if (incluirEndereco && p.Endereco is not null)
        {
            payer["address"] = new JsonObject
            {
                ["zip_code"] = p.Endereco.Cep,
                ["street_name"] = p.Endereco.Logradouro,
                ["street_number"] = p.Endereco.Numero,
                ["neighborhood"] = p.Endereco.Bairro,
                ["city"] = p.Endereco.Cidade,
                ["federal_unit"] = p.Endereco.Uf
            };
        }
        return payer;
    }

    private static string FormatarData(DateTimeOffset data) =>
        data.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ envio

    private async Task<RespostaGateway> CriarAsync(JsonObject corpo, Guid chave, CancellationToken ct)
    {
        var json = corpo.ToJsonString(Json);

        // Em timeout ou queda de rede reenviamos com a MESMA chave de idempotência:
        // se o pagamento já tinha sido criado, o Mercado Pago devolve o mesmo, sem cobrar duas vezes.
        for (var tentativa = 1; ; tentativa++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/payments")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-Idempotency-Key", chave.ToString());
            try
            {
                using var doc = await EnviarAsync(req, ct);
                return Ler(doc.RootElement);
            }
            catch (GatewayPagamentoException ex) when (tentativa == 1 && ex.InnerException is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Falha de comunicação ao criar pagamento; repetindo com a mesma chave de idempotência");
            }
        }
    }

    private async Task<JsonDocument> EnviarAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.AccessToken))
            throw new GatewayPagamentoException("Mercado Pago não configurado (MercadoPago:AccessToken).");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opcoes.AccessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new GatewayPagamentoException("Erro de rede ao chamar o Mercado Pago.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new GatewayPagamentoException("Tempo esgotado ao chamar o Mercado Pago.", ex);
        }

        using (resp)
        {
            var conteudo = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                // O corpo de erro do Mercado Pago não contém dados de cartão; ajuda no diagnóstico
                _logger.LogWarning("Mercado Pago {Metodo} {Url} respondeu {Status}: {Corpo}",
                    req.Method, req.RequestUri, (int)resp.StatusCode, conteudo.Length > 2000 ? conteudo[..2000] : conteudo);
                throw new GatewayPagamentoException(resp.StatusCode == HttpStatusCode.Unauthorized
                    ? "Credencial do Mercado Pago inválida."
                    : $"Mercado Pago respondeu {(int)resp.StatusCode}.");
            }
            try
            {
                return JsonDocument.Parse(conteudo);
            }
            catch (JsonException ex)
            {
                throw new GatewayPagamentoException("Resposta inválida do Mercado Pago.", ex);
            }
        }
    }

    // ------------------------------------------------------------------ leitura

    internal static RespostaGateway Ler(JsonElement r)
    {
        if (!r.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
            throw new GatewayPagamentoException("Resposta do Mercado Pago sem id de pagamento.");

        string? qr = null, qrBase64 = null, boletoUrl = null, linha = null, ultimos = null;

        if (r.TryGetProperty("point_of_interaction", out var poi) && poi.ValueKind == JsonValueKind.Object
            && poi.TryGetProperty("transaction_data", out var td) && td.ValueKind == JsonValueKind.Object)
        {
            qr = Texto(td, "qr_code");
            qrBase64 = Texto(td, "qr_code_base64");
            boletoUrl = Texto(td, "ticket_url");
        }

        if (r.TryGetProperty("transaction_details", out var det) && det.ValueKind == JsonValueKind.Object)
        {
            boletoUrl = Texto(det, "external_resource_url") ?? boletoUrl;
            linha = Texto(det, "digitable_line");
        }
        if (linha is null && r.TryGetProperty("barcode", out var bc) && bc.ValueKind == JsonValueKind.Object)
            linha = Texto(bc, "content");

        if (r.TryGetProperty("card", out var card) && card.ValueKind == JsonValueKind.Object)
            ultimos = Texto(card, "last_four_digits");

        DateTimeOffset? expiracao = null;
        if (Texto(r, "date_of_expiration") is { } exp &&
            DateTimeOffset.TryParse(exp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            expiracao = dto;

        int? parcelas = r.TryGetProperty("installments", out var inst) && inst.ValueKind == JsonValueKind.Number ? inst.GetInt32() : null;
        var valor = r.TryGetProperty("transaction_amount", out var amt) && amt.ValueKind == JsonValueKind.Number ? amt.GetDecimal() : 0m;

        return new RespostaGateway(
            id,
            Texto(r, "status") ?? "pending",
            Texto(r, "status_detail"),
            Texto(r, "external_reference"),
            valor,
            Texto(r, "payment_method_id"),
            qr, qrBase64, boletoUrl, linha, expiracao, parcelas, ultimos);
    }

    private static string? Texto(JsonElement e, string nome)
    {
        if (!e.TryGetProperty(nome, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
    }
}
