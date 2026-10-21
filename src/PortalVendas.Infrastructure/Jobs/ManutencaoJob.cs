using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Pedidos;

namespace PortalVendas.Infrastructure.Jobs;

/// <summary>
/// Executa a manutenção periódica (expiração, conciliação, limpeza) dentro do processo do site (ADR-007).
/// No IIS, o Application Pool precisa de startMode=AlwaysRunning e preloadEnabled=true.
/// </summary>
public class ManutencaoJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ManutencaoJob> _logger;
    private readonly TimeSpan _intervalo;
    private readonly bool _habilitado;

    public ManutencaoJob(IServiceScopeFactory scopes, IConfiguration configuracao, ILogger<ManutencaoJob> logger)
    {
        _scopes = scopes;
        _logger = logger;
        _intervalo = TimeSpan.FromMinutes(Math.Max(1, configuracao.GetValue("Manutencao:IntervaloMinutos", 5)));
        _habilitado = configuracao.GetValue("Manutencao:Habilitada", true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_habilitado)
        {
            _logger.LogWarning("Job de manutenção desabilitado por configuração");
            return;
        }

        // Espera o site terminar de subir
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(_intervalo);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var servico = scope.ServiceProvider.GetRequiredService<ManutencaoService>();
                await servico.ExecutarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no job de manutenção");
            }
        }
        while (await WaitSafe(timer, stoppingToken));
    }

    private static async Task<bool> WaitSafe(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
