using PortalVendas.Application.WhatsApp;

namespace PortalVendas.Infrastructure.WhatsApp;

/// <summary>
/// Provedor de teste: não envia nada. As respostas do robô são gravadas no histórico da conversa
/// (como qualquer mensagem enviada) e aparecem no simulador do painel.
/// </summary>
public sealed class ConectorSimulador : IConectorWhatsApp
{
    public ProvedorWhatsApp Provedor => ProvedorWhatsApp.Simulador;

    public Task<ResultadoEnvio> EnviarAsync(string telefone, MensagemSaida mensagem, CancellationToken ct = default) =>
        Task.FromResult(ResultadoEnvio.Ok("sim:" + Guid.NewGuid().ToString("N")));

    public Task<ResultadoEnvio> EnviarModeloAsync(string telefone, string modelo, string idioma, IReadOnlyList<string> parametros,
        string textoAlternativo, CancellationToken ct = default) =>
        Task.FromResult(ResultadoEnvio.Ok("sim:" + Guid.NewGuid().ToString("N")));
}
