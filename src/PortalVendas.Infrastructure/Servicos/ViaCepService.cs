using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Domain.Comum;

namespace PortalVendas.Infrastructure.Servicos;

/// <summary>RF07: preenchimento de endereço pelo CEP (https://viacep.com.br).</summary>
public class ViaCepService : ICepService
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ViaCepService> _logger;

    public ViaCepService(HttpClient http, IMemoryCache cache, ILogger<ViaCepService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<EnderecoCep?> ConsultarAsync(string cep, CancellationToken ct = default)
    {
        var normalizado = Cep.Normalizar(cep);
        if (!Cep.EhValido(normalizado)) return null;

        var chave = "cep:" + normalizado;
        if (_cache.TryGetValue(chave, out EnderecoCep? emCache)) return emCache;

        try
        {
            using var resp = await _http.GetAsync($"ws/{normalizado}/json/", ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var r = doc.RootElement;
            EnderecoCep? resultado = null;
            if (!r.TryGetProperty("erro", out _))
            {
                string Ler(string nome) => r.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                resultado = new EnderecoCep(normalizado, Ler("logradouro"), Ler("bairro"), Ler("localidade"), Ler("uf"));
            }

            // CEP inexistente fica em cache por menos tempo
            _cache.Set(chave, resultado, resultado is null ? TimeSpan.FromHours(1) : TimeSpan.FromHours(24));
            return resultado;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "ViaCEP indisponível para o CEP {Cep}", normalizado);
            return null;
        }
    }
}
