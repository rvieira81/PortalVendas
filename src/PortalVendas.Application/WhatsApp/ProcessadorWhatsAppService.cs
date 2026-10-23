using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Domain.WhatsApp;

namespace PortalVendas.Application.WhatsApp;

/// <summary>
/// Caminho de uma mensagem recebida: o webhook grava (deduplicando pelo id do provedor) e põe na fila;
/// o processamento em segundo plano chama o robô e envia as respostas.
/// </summary>
public sealed class ProcessadorWhatsAppService
{
    private readonly IAppDbContext _db;
    private readonly ConfiguracaoWhatsAppService _configuracao;
    private readonly AtendimentoWhatsAppService _atendimento;
    private readonly EnvioWhatsAppService _envio;
    private readonly IRelogio _relogio;
    private readonly ILogger<ProcessadorWhatsAppService> _logger;

    public ProcessadorWhatsAppService(IAppDbContext db, ConfiguracaoWhatsAppService configuracao, AtendimentoWhatsAppService atendimento,
        EnvioWhatsAppService envio, IRelogio relogio, ILogger<ProcessadorWhatsAppService> logger)
    {
        _db = db;
        _configuracao = configuracao;
        _atendimento = atendimento;
        _envio = envio;
        _relogio = relogio;
        _logger = logger;
    }

    /// <summary>Grava a mensagem recebida. Devolve o id para a fila, ou null se é repetida (o provedor reenviou).</summary>
    public async Task<long?> RegistrarEntradaAsync(MensagemRecebida m, CancellationToken ct = default)
    {
        var telefone = TelefoneWhatsApp.Normalizar(m.Telefone);
        if (telefone.Length < 10 || string.IsNullOrWhiteSpace(m.IdExterno)) return null;
        var idExterno = m.IdExterno.Length > 200 ? m.IdExterno[..200] : m.IdExterno;
        if (await _db.MensagensWhatsApp.AnyAsync(x => x.IdExterno == idExterno, ct)) return null;

        var agora = _relogio.AgoraUtc;
        var conversa = await _db.ConversasWhatsApp.FirstOrDefaultAsync(c => c.Telefone == telefone, ct);
        if (conversa is null)
        {
            conversa = new ConversaWhatsApp { Telefone = telefone, CriadoEm = agora, UltimaMensagemEm = agora };
            _db.ConversasWhatsApp.Add(conversa);
        }
        if (!string.IsNullOrWhiteSpace(m.Nome)) conversa.NomeContato = m.Nome.Trim().Length > 150 ? m.Nome.Trim()[..150] : m.Nome.Trim();
        conversa.NaoLidas++;
        conversa.UltimaMensagemEm = agora;
        // UltimaEntradaEm é atualizada no processamento (o robô precisa saber quando foi a mensagem anterior)

        var texto = string.IsNullOrWhiteSpace(m.Texto) ? "[mensagem sem texto]" : m.Texto;
        var mensagem = new MensagemWhatsApp
        {
            Entrada = true,
            Texto = texto.Length > 4000 ? texto[..4000] : texto,
            IdExterno = idExterno,
            Autor = m.IdOpcao,   // guarda o id da opção tocada (o texto mostra o título)
            CriadoEm = agora
        };
        conversa.Mensagens.Add(mensagem);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Mesma mensagem chegando em paralelo (UNIQUE do IdExterno) ou a conversa criada ao mesmo tempo
            _db.ChangeTracker.Clear();
            if (await _db.MensagensWhatsApp.AnyAsync(x => x.IdExterno == idExterno, ct)) return null;
            throw;
        }
        return mensagem.Id;
    }

    public Task<List<long>> PendentesAsync(TimeSpan janela, CancellationToken ct = default)
    {
        var desde = _relogio.AgoraUtc - janela;
        return _db.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.Entrada && !m.Processada && m.CriadoEm >= desde)
            .OrderBy(m => m.Id)
            .Select(m => m.Id)
            .ToListAsync(ct);
    }

    public async Task ProcessarAsync(long mensagemId, CancellationToken ct = default)
    {
        var mensagem = await _db.MensagensWhatsApp.FirstOrDefaultAsync(m => m.Id == mensagemId, ct);
        if (mensagem is null || mensagem.Processada || !mensagem.Entrada) return;
        var conversa = await _db.ConversasWhatsApp.FirstAsync(c => c.Id == mensagem.ConversaId, ct);

        var anterior = conversa.UltimaEntradaEm;
        conversa.UltimaEntradaEm = mensagem.CriadoEm;

        string? erro = null;
        try
        {
            var config = await _configuracao.ObterAsync(ct);
            if (!_configuracao.Licenciado)
                erro = "Módulo WhatsApp fora da licença: mensagem não respondida.";
            else if (conversa.AtendimentoHumano || !config.RoboAtivo || config.Provedor == ProvedorWhatsApp.Desativado)
            {
                // Atendente cuidando da conversa (ou robô desligado): a mensagem só aparece no painel
            }
            else
            {
                var expirado = anterior is not null && mensagem.CriadoEm - anterior.Value > ConversaWhatsApp.ExpiracaoContexto;
                var recebida = new MensagemRecebida(conversa.Telefone, conversa.NomeContato, mensagem.Texto,
                    mensagem.Autor, mensagem.IdExterno ?? "", mensagem.CriadoEm);
                var respostas = await _atendimento.ResponderAsync(conversa, recebida, expirado, ct);
                await _db.SaveChangesAsync(ct);   // estado do robô antes de enviar (se o envio falhar, o contexto não se perde)
                var envio = await _envio.EnviarAsync(conversa, respostas, EnvioWhatsAppService.AutorRobo, ct);
                if (!envio.Sucesso) erro = envio.Erro;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Erro no robô do WhatsApp (mensagem {MensagemId}, conversa {ConversaId})", mensagemId, conversa.Id);
            erro = "Erro interno ao responder: " + ex.Message;
            _db.ChangeTracker.Clear();
            conversa = await _db.ConversasWhatsApp.FirstAsync(c => c.Id == conversa.Id, ct);
            mensagem = await _db.MensagensWhatsApp.FirstAsync(m => m.Id == mensagemId, ct);
            conversa.UltimaEntradaEm = mensagem.CriadoEm;
        }

        mensagem.Processada = true;
        mensagem.Erro = erro is { Length: > 500 } ? erro[..500] : erro;
        await _db.SaveChangesAsync(ct);
    }
}
