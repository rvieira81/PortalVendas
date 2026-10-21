using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Frete;

namespace PortalVendas.Application.Admin;

/// <summary>RF19: tabela de contingência de frete por faixa de CEP.</summary>
public class FaixaFreteService
{
    private readonly IAppDbContext _db;
    private readonly IRelogio _relogio;

    public FaixaFreteService(IAppDbContext db, IRelogio relogio)
    {
        _db = db;
        _relogio = relogio;
    }

    public Task<List<FaixaFrete>> ListarAsync(CancellationToken ct = default) =>
        _db.FaixasFrete.AsNoTracking().OrderBy(f => f.CepInicio).ToListAsync(ct);

    public async Task<Resultado> SalvarAsync(FaixaFrete dados, CancellationToken ct = default)
    {
        var inicio = Cep.Normalizar(dados.CepInicio);
        var fim = Cep.Normalizar(dados.CepFim);
        var descricao = Texto.Limpar(dados.Descricao);
        if (string.IsNullOrEmpty(descricao) || descricao.Length > 100) return Resultado.Falha("Informe uma descrição de até 100 caracteres.");
        if (inicio.Length != 8 || fim.Length != 8) return Resultado.Falha("Informe CEPs com 8 dígitos.");
        if (string.CompareOrdinal(inicio, fim) > 0) return Resultado.Falha("O CEP inicial deve ser menor ou igual ao final.");
        if (dados.Valor < 0) return Resultado.Falha("O valor não pode ser negativo.");
        if (dados.PrazoDias is < 1 or > 90) return Resultado.Falha("O prazo deve estar entre 1 e 90 dias.");

        var agora = _relogio.AgoraUtc;
        FaixaFrete faixa;
        if (dados.Id == 0)
        {
            faixa = new FaixaFrete { CriadoEm = agora };
            _db.FaixasFrete.Add(faixa);
        }
        else
        {
            var existente = await _db.FaixasFrete.FirstOrDefaultAsync(f => f.Id == dados.Id, ct);
            if (existente is null) return Resultado.Falha("Faixa não encontrada.");
            faixa = existente;
            faixa.AlteradoEm = agora;
        }

        faixa.Descricao = descricao;
        faixa.CepInicio = inicio;
        faixa.CepFim = fim;
        faixa.Valor = Math.Round(dados.Valor, 2);
        faixa.PrazoDias = dados.PrazoDias;
        faixa.Ativa = dados.Ativa;
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public async Task ExcluirAsync(int id, CancellationToken ct = default) =>
        await _db.FaixasFrete.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
}
