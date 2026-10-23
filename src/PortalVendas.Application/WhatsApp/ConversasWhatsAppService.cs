using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Domain.WhatsApp;

namespace PortalVendas.Application.WhatsApp;

public sealed record ConversaResumo(int Id, string Telefone, string? Nome, string? Cliente, bool AtendimentoHumano, int NaoLidas,
    DateTime UltimaMensagemEm, string? UltimoTexto, bool UltimaFoiDoCliente);

public sealed record MensagemPainel(long Id, bool Entrada, string Texto, string? Autor, string? Erro, DateTime CriadoEm);

public sealed record PedidoDaConversa(int Id, DateTime CriadoEm, StatusPedido Status, decimal Total, OrigemPedido Origem)
{
    public string Numero => Id.ToString("D6");
}

public sealed record ConversaDetalhe(ConversaResumo Resumo, EtapaConversa Etapa, bool DentroDaJanela, IReadOnlyList<MensagemPainel> Mensagens,
    IReadOnlyList<PedidoDaConversa> Pedidos);

/// <summary>Painel de atendimento: lista de conversas, histórico, resposta do atendente e devolução ao robô.</summary>
public sealed class ConversasWhatsAppService
{
    public const int TamanhoPagina = 30;

    private readonly IAppDbContext _db;
    private readonly EnvioWhatsAppService _envio;
    private readonly IFabricaConectorWhatsApp _fabrica;
    private readonly IRelogio _relogio;

    public ConversasWhatsAppService(IAppDbContext db, EnvioWhatsAppService envio, IFabricaConectorWhatsApp fabrica, IRelogio relogio)
    {
        _db = db;
        _envio = envio;
        _fabrica = fabrica;
        _relogio = relogio;
    }

    public async Task<Pagina<ConversaResumo>> ListarAsync(bool somenteAtendente, string? termo, int pagina, CancellationToken ct = default)
    {
        pagina = Pagina<ConversaResumo>.Normalizar(pagina);
        var consulta = _db.ConversasWhatsApp.AsNoTracking().AsQueryable();
        if (somenteAtendente) consulta = consulta.Where(c => c.AtendimentoHumano);
        var t = termo?.Trim();
        if (!string.IsNullOrEmpty(t))
        {
            var digitos = Domain.Comum.Texto.SomenteDigitos(t);
            consulta = digitos.Length >= 4
                ? consulta.Where(c => c.Telefone.Contains(digitos))
                : consulta.Where(c => c.NomeContato != null && c.NomeContato.Contains(t));
        }

        var total = await consulta.CountAsync(ct);
        var itens = await consulta
            .OrderByDescending(c => c.AtendimentoHumano && c.NaoLidas > 0)
            .ThenByDescending(c => c.UltimaMensagemEm)
            .Skip((pagina - 1) * TamanhoPagina).Take(TamanhoPagina)
            .Select(c => new
            {
                c.Id, c.Telefone, c.NomeContato, c.UsuarioId, c.AtendimentoHumano, c.NaoLidas, c.UltimaMensagemEm,
                Ultima = c.Mensagens.OrderByDescending(m => m.Id).Select(m => new { m.Texto, m.Entrada }).FirstOrDefault()
            })
            .ToListAsync(ct);

        var ids = itens.Where(i => i.UsuarioId != null).Select(i => i.UsuarioId!.Value).ToList();
        var clientes = await _db.Usuarios.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct);

        return new Pagina<ConversaResumo>(itens.Select(i => new ConversaResumo(i.Id, i.Telefone, i.NomeContato,
            i.UsuarioId is { } u && clientes.TryGetValue(u, out var nome) ? nome : null,
            i.AtendimentoHumano, i.NaoLidas, i.UltimaMensagemEm, i.Ultima?.Texto, i.Ultima?.Entrada ?? false)).ToList(),
            pagina, TamanhoPagina, total);
    }

    public Task<int> ContarAguardandoAtendenteAsync(CancellationToken ct = default) =>
        _db.ConversasWhatsApp.CountAsync(c => c.AtendimentoHumano && c.NaoLidas > 0, ct);

    /// <summary>Abre a conversa no painel e zera as não lidas.</summary>
    public async Task<ConversaDetalhe?> AbrirAsync(int id, CancellationToken ct = default)
    {
        var c = await _db.ConversasWhatsApp.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        if (c.NaoLidas > 0)
        {
            c.NaoLidas = 0;
            await _db.SaveChangesAsync(ct);
        }

        var mensagens = await _db.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.ConversaId == id)
            .OrderByDescending(m => m.Id).Take(200)
            .Select(m => new MensagemPainel(m.Id, m.Entrada, m.Texto, m.Entrada ? null : m.Autor, m.Erro, m.CriadoEm))
            .ToListAsync(ct);
        mensagens.Reverse();

        var cliente = c.UsuarioId is null ? null
            : await _db.Usuarios.AsNoTracking().Where(u => u.Id == c.UsuarioId).Select(u => u.Nome).FirstOrDefaultAsync(ct);
        var pedidos = c.UsuarioId is null ? new List<PedidoDaConversa>()
            : await _db.Pedidos.AsNoTracking().Where(p => p.UsuarioId == c.UsuarioId)
                .OrderByDescending(p => p.CriadoEm).Take(10)
                .Select(p => new PedidoDaConversa(p.Id, p.CriadoEm, p.Status, p.Total, p.Origem))
                .ToListAsync(ct);

        var ultima = mensagens.LastOrDefault();
        return new ConversaDetalhe(
            new ConversaResumo(c.Id, c.Telefone, c.NomeContato, cliente, c.AtendimentoHumano, 0, c.UltimaMensagemEm, ultima?.Texto, ultima?.Entrada ?? false),
            c.Etapa, c.DentroDaJanela(_relogio.AgoraUtc), mensagens, pedidos);
    }

    /// <summary>Resposta do atendente. Assume a conversa (o robô para de responder até ser devolvida).</summary>
    public async Task<Resultado> ResponderAsync(int id, string texto, string autor, CancellationToken ct = default)
    {
        var t = texto?.Trim();
        if (string.IsNullOrEmpty(t)) return Resultado.Falha("Escreva a mensagem.");
        if (t.Length > 4000) return Resultado.Falha("Mensagem muito longa (máximo 4000 caracteres).");
        var c = await _db.ConversasWhatsApp.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Resultado.Falha("Conversa não encontrada.");

        var conector = await _fabrica.CriarAsync(ct);
        if (conector is null) return Resultado.Falha("O WhatsApp está desligado ou sem configuração.");
        if (conector.Provedor == ProvedorWhatsApp.Meta && !c.DentroDaJanela(_relogio.AgoraUtc))
            return Resultado.Falha("Passaram mais de 24 h desde a última mensagem do cliente: pela regra da Meta só é possível responder depois que ele escrever de novo.");

        c.AtendimentoHumano = true;
        c.NaoLidas = 0;
        var r = await _envio.EnviarAsync(c, new[] { MensagemSaida.Simples(t) }, autor, ct);
        await _db.SaveChangesAsync(ct);
        return r.Sucesso ? Resultado.Ok() : Resultado.Falha("O WhatsApp recusou a mensagem: " + r.Erro);
    }

    public async Task<Resultado> AssumirAsync(int id, CancellationToken ct = default)
    {
        var c = await _db.ConversasWhatsApp.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Resultado.Falha("Conversa não encontrada.");
        c.AtendimentoHumano = true;
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>Devolve ao robô: ele volta a responder a partir do menu.</summary>
    public async Task<Resultado> DevolverAoRoboAsync(int id, bool avisarCliente, CancellationToken ct = default)
    {
        var c = await _db.ConversasWhatsApp.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Resultado.Falha("Conversa não encontrada.");
        c.AtendimentoHumano = false;
        c.Etapa = EtapaConversa.Menu;
        var estado = EstadoConversa.Ler(c.Contexto);
        estado.Recomecar();
        c.Contexto = estado.Gravar();
        await _db.SaveChangesAsync(ct);

        if (avisarCliente && c.DentroDaJanela(_relogio.AgoraUtc))
            await _envio.EnviarAsync(c, new[] { MensagemSaida.Simples("Se precisar de mais alguma coisa, é só mandar *menu*. 😉") },
                EnvioWhatsAppService.AutorRobo, ct);
        return Resultado.Ok();
    }
}
