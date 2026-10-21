using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Usuarios;

namespace PortalVendas.Application.Contas;

/// <summary>RF07: endereços do cliente. Toda operação filtra pelo dono.</summary>
public class EnderecoService
{
    public const int MaximoPorCliente = 10;
    private readonly IAppDbContext _db;
    private readonly IRelogio _relogio;

    public EnderecoService(IAppDbContext db, IRelogio relogio)
    {
        _db = db;
        _relogio = relogio;
    }

    public Task<List<Endereco>> ListarAsync(int usuarioId, CancellationToken ct = default) =>
        _db.Enderecos.AsNoTracking()
            .Where(e => e.UsuarioId == usuarioId)
            .OrderByDescending(e => e.Padrao).ThenBy(e => e.Apelido)
            .ToListAsync(ct);

    public Task<Endereco?> ObterAsync(int usuarioId, int enderecoId, CancellationToken ct = default) =>
        _db.Enderecos.AsNoTracking().FirstOrDefaultAsync(e => e.Id == enderecoId && e.UsuarioId == usuarioId, ct);

    /// <summary>Cria (Id = 0) ou atualiza. Devolve o Id.</summary>
    public async Task<Resultado<int>> SalvarAsync(int usuarioId, Endereco dados, CancellationToken ct = default)
    {
        dados.Normalizar();
        var erros = dados.Validar();
        if (erros.Count > 0) return Resultado<int>.Falha(string.Join(" ", erros));

        var agora = _relogio.AgoraUtc;
        var todos = await _db.Enderecos.Where(e => e.UsuarioId == usuarioId).ToListAsync(ct);

        Endereco alvo;
        if (dados.Id == 0)
        {
            if (todos.Count >= MaximoPorCliente) return Resultado<int>.Falha($"Limite de {MaximoPorCliente} endereços atingido.");
            alvo = new Endereco { UsuarioId = usuarioId, CriadoEm = agora };
            _db.Enderecos.Add(alvo);
        }
        else
        {
            var existente = todos.FirstOrDefault(e => e.Id == dados.Id);
            if (existente is null) return Resultado<int>.Falha("Endereço não encontrado.");
            alvo = existente;
            alvo.AlteradoEm = agora;
        }

        alvo.Apelido = dados.Apelido;
        alvo.Destinatario = dados.Destinatario;
        alvo.Cep = dados.Cep;
        alvo.Logradouro = dados.Logradouro;
        alvo.Numero = dados.Numero;
        alvo.Complemento = dados.Complemento;
        alvo.Bairro = dados.Bairro;
        alvo.Cidade = dados.Cidade;
        alvo.Uf = dados.Uf;

        // O primeiro endereço vira padrão automaticamente; marcar outro como padrão desmarca os demais
        var outros = todos.Where(e => !ReferenceEquals(e, alvo)).ToList();
        if (dados.Padrao || outros.Count == 0 || !outros.Any(e => e.Padrao))
        {
            foreach (var e in outros) e.Padrao = false;
            alvo.Padrao = true;
        }

        await _db.SaveChangesAsync(ct);
        return Resultado<int>.Ok(alvo.Id);
    }

    public async Task<Resultado> DefinirPadraoAsync(int usuarioId, int enderecoId, CancellationToken ct = default)
    {
        var todos = await _db.Enderecos.Where(e => e.UsuarioId == usuarioId).ToListAsync(ct);
        if (!todos.Any(e => e.Id == enderecoId)) return Resultado.Falha("Endereço não encontrado.");
        foreach (var e in todos) e.Padrao = e.Id == enderecoId;
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ExcluirAsync(int usuarioId, int enderecoId, CancellationToken ct = default)
    {
        var todos = await _db.Enderecos.Where(e => e.UsuarioId == usuarioId).ToListAsync(ct);
        var alvo = todos.FirstOrDefault(e => e.Id == enderecoId);
        if (alvo is null) return Resultado.Falha("Endereço não encontrado.");

        _db.Enderecos.Remove(alvo);
        if (alvo.Padrao)
        {
            var novoPadrao = todos.Where(e => e.Id != enderecoId).OrderBy(e => e.Id).FirstOrDefault();
            if (novoPadrao is not null) novoPadrao.Padrao = true;
        }
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }
}
