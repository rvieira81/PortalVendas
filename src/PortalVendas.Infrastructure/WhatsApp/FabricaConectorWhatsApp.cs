using PortalVendas.Application.WhatsApp;

namespace PortalVendas.Infrastructure.WhatsApp;

public sealed class FabricaConectorWhatsApp : IFabricaConectorWhatsApp
{
    public const string NomeCliente = "whatsapp";

    private readonly IHttpClientFactory _http;
    private readonly ConfiguracaoWhatsAppService _configuracao;

    public FabricaConectorWhatsApp(IHttpClientFactory http, ConfiguracaoWhatsAppService configuracao)
    {
        _http = http;
        _configuracao = configuracao;
    }

    public async Task<IConectorWhatsApp?> CriarAsync(CancellationToken ct = default)
    {
        if (!_configuracao.Licenciado) return null;
        var c = await _configuracao.ObterAsync(ct);
        return c.Provedor switch
        {
            ProvedorWhatsApp.Meta when c.MetaCompleta => new ConectorMeta(Cliente(ConectorMeta.UrlGraph), c.MetaPhoneNumberId!, c.MetaToken!),
            ProvedorWhatsApp.Evolution when c.EvolutionCompleta => new ConectorEvolution(Cliente(null), c.EvolutionUrl!, c.EvolutionInstancia!, c.EvolutionApiKey!),
            _ => null
        };
    }

    private HttpClient Cliente(string? baseUrl)
    {
        var http = _http.CreateClient(NomeCliente);
        if (baseUrl is not null) http.BaseAddress = new Uri(baseUrl);
        return http;
    }
}
