using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.WhatsApp;

namespace PortalVendas.Infrastructure.WhatsApp;

/// <summary>Fila em memória. Se o site reiniciar, as mensagens não processadas da última hora são retomadas.</summary>
public sealed class FilaWhatsApp : IFilaWhatsApp
{
    private readonly Channel<long> _canal = Channel.CreateBounded<long>(new BoundedChannelOptions(5000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true
    });

    public void Enfileirar(long mensagemId) => _canal.Writer.TryWrite(mensagemId);

    internal ChannelReader<long> Leitor => _canal.Reader;
}

/// <summary>Responde as mensagens em ordem (um leitor só: as mensagens de um cliente nunca são respondidas fora de ordem).</summary>
public sealed class ProcessadorWhatsAppJob : BackgroundService
{
    private readonly FilaWhatsApp _fila;
    private readonly IServiceScopeFactory _escopos;
    private readonly ILogger<ProcessadorWhatsAppJob> _logger;

    public ProcessadorWhatsAppJob(FilaWhatsApp fila, IServiceScopeFactory escopos, ILogger<ProcessadorWhatsAppJob> logger)
    {
        _fila = fila;
        _escopos = escopos;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RetomarPendentesAsync(stoppingToken);

        await foreach (var id in _fila.Leitor.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var escopo = _escopos.CreateScope();
                await escopo.ServiceProvider.GetRequiredService<ProcessadorWhatsAppService>().ProcessarAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar a mensagem {MensagemId} do WhatsApp", id);
            }
        }
    }

    private async Task RetomarPendentesAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            using var escopo = _escopos.CreateScope();
            var pendentes = await escopo.ServiceProvider.GetRequiredService<ProcessadorWhatsAppService>().PendentesAsync(TimeSpan.FromHours(1), ct);
            foreach (var id in pendentes) _fila.Enfileirar(id);
            if (pendentes.Count > 0) _logger.LogInformation("WhatsApp: {Quantidade} mensagens retomadas após reinício", pendentes.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível retomar as mensagens pendentes do WhatsApp");
        }
    }
}
