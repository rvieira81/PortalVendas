using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Domain.WhatsApp;

namespace PortalVendas.Application.WhatsApp;

/// <summary>Envia pelo provedor configurado e grava cada mensagem enviada no histórico da conversa.</summary>
public sealed class EnvioWhatsAppService
{
    public const string AutorRobo = "robô";

    private readonly IAppDbContext _db;
    private readonly IFabricaConectorWhatsApp _fabrica;
    private readonly ConfiguracaoWhatsAppService _configuracao;
    private readonly IRelogio _relogio;
    private readonly ILogger<EnvioWhatsAppService> _logger;

    public EnvioWhatsAppService(IAppDbContext db, IFabricaConectorWhatsApp fabrica, ConfiguracaoWhatsAppService configuracao,
        IRelogio relogio, ILogger<EnvioWhatsAppService> logger)
    {
        _db = db;
        _fabrica = fabrica;
        _configuracao = configuracao;
        _relogio = relogio;
        _logger = logger;
    }

    /// <summary>Envia em sequência (a ordem importa). Para no primeiro erro. Não grava o contexto da conversa (quem chama grava).</summary>
    public async Task<ResultadoEnvio> EnviarAsync(ConversaWhatsApp conversa, IReadOnlyList<MensagemSaida> mensagens, string autor,
        CancellationToken ct = default)
    {
        if (mensagens.Count == 0) return ResultadoEnvio.Ok(null);
        var conector = await _fabrica.CriarAsync(ct);
        if (conector is null) return ResultadoEnvio.Falha("WhatsApp desligado ou não configurado.");

        ResultadoEnvio ultimo = ResultadoEnvio.Ok(null);
        foreach (var m in mensagens)
        {
            ultimo = await EnviarUmaAsync(conector, conversa, m, autor, ct);
            if (!ultimo.Sucesso) break;
        }
        return ultimo;
    }

    /// <summary>
    /// Aviso iniciado pela loja (status do pedido). Na API oficial, fora da janela de 24 h só é possível
    /// enviar um modelo aprovado; sem modelo configurado o aviso é registrado e não enviado.
    /// </summary>
    public async Task<ResultadoEnvio> EnviarAvisoAsync(ConversaWhatsApp conversa, string texto, IReadOnlyList<string> parametrosModelo,
        CancellationToken ct = default)
    {
        var conector = await _fabrica.CriarAsync(ct);
        if (conector is null) return ResultadoEnvio.Falha("WhatsApp desligado ou não configurado.");

        var agora = _relogio.AgoraUtc;
        if (conector.Provedor != ProvedorWhatsApp.Meta || conversa.DentroDaJanela(agora))
            return await EnviarUmaAsync(conector, conversa, MensagemSaida.Simples(texto), AutorRobo, ct);

        var config = await _configuracao.ObterAsync(ct);
        if (string.IsNullOrEmpty(config.MetaModeloAviso))
        {
            Registrar(conversa, texto, AutorRobo, null, "Não enviado: fora da janela de 24 h e sem modelo de aviso configurado.");
            await _db.SaveChangesAsync(ct);
            return ResultadoEnvio.Falha("Fora da janela de 24 h da Meta e sem modelo de aviso configurado.");
        }

        ResultadoEnvio r;
        try
        {
            r = await conector.EnviarModeloAsync(conversa.Telefone, config.MetaModeloAviso, config.MetaIdiomaModelo, parametrosModelo, texto, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao enviar modelo para a conversa {ConversaId}", conversa.Id);
            r = ResultadoEnvio.Falha("Falha de comunicação com o WhatsApp.");
        }
        Registrar(conversa, texto, AutorRobo, r.IdExterno, r.Sucesso ? null : r.Erro);
        await _db.SaveChangesAsync(ct);
        return r;
    }

    private async Task<ResultadoEnvio> EnviarUmaAsync(IConectorWhatsApp conector, ConversaWhatsApp conversa, MensagemSaida m, string autor,
        CancellationToken ct)
    {
        ResultadoEnvio r;
        try
        {
            r = await conector.EnviarAsync(conversa.Telefone, m, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao enviar mensagem para a conversa {ConversaId}", conversa.Id);
            r = ResultadoEnvio.Falha("Falha de comunicação com o WhatsApp.");
        }
        if (!r.Sucesso) _logger.LogWarning("WhatsApp recusou a mensagem da conversa {ConversaId}: {Erro}", conversa.Id, r.Erro);

        var texto = m.ImagemUrl is null ? m.TextoComOpcoes() : "[foto] " + m.TextoComOpcoes();
        Registrar(conversa, texto, autor, r.IdExterno, r.Sucesso ? null : r.Erro);
        await _db.SaveChangesAsync(ct);
        return r;
    }

    private void Registrar(ConversaWhatsApp conversa, string texto, string autor, string? idExterno, string? erro)
    {
        var agora = _relogio.AgoraUtc;
        _db.MensagensWhatsApp.Add(new MensagemWhatsApp
        {
            ConversaId = conversa.Id,
            Entrada = false,
            Texto = texto.Length > 4000 ? texto[..4000] : texto,
            IdExterno = idExterno is { Length: > 200 } ? idExterno[..200] : idExterno,
            Autor = autor.Length > 100 ? autor[..100] : autor,
            Processada = true,
            Erro = erro is { Length: > 500 } ? erro[..500] : erro,
            CriadoEm = agora
        });
        conversa.UltimaMensagemEm = agora;
    }
}
