using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Frete;

namespace PortalVendas.Application.Admin;

public record ConfiguracaoLoja(
    string CepOrigem,
    int DiasManuseio,
    decimal FreteGratisAPartirDe,
    int ParcelasMaximas,
    decimal ParcelaMinima)
{
    public bool TemFreteGratis => FreteGratisAPartirDe > 0;

    /// <summary>RN06: máximo de parcelas respeitando a parcela mínima.</summary>
    public int ParcelasPermitidas(decimal total)
    {
        if (ParcelaMinima <= 0) return Math.Max(1, ParcelasMaximas);
        var pelaParcelaMinima = (int)Math.Floor(total / ParcelaMinima);
        return Math.Clamp(pelaParcelaMinima, 1, Math.Max(1, ParcelasMaximas));
    }
}

public class ConfiguracaoLojaService
{
    public const string ChaveCepOrigem = "Frete.CepOrigem";
    public const string ChaveDiasManuseio = "Frete.DiasManuseio";
    public const string ChaveFreteGratis = "Frete.GratisAPartirDe";
    public const string ChaveParcelasMaximas = "Pagamento.ParcelasMaximas";
    public const string ChaveParcelaMinima = "Pagamento.ParcelaMinima";

    private readonly IAppDbContext _db;
    private readonly IRelogio _relogio;

    public ConfiguracaoLojaService(IAppDbContext db, IRelogio relogio)
    {
        _db = db;
        _relogio = relogio;
    }

    public async Task<ConfiguracaoLoja> ObterAsync(CancellationToken ct = default)
    {
        var valores = await _db.Configuracoes.AsNoTracking().ToDictionaryAsync(c => c.Chave, c => c.Valor, ct);

        string LerTexto(string chave, string padrao) => valores.TryGetValue(chave, out var v) && !string.IsNullOrWhiteSpace(v) ? v : padrao;
        int LerInteiro(string chave, int padrao) =>
            int.TryParse(LerTexto(chave, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : padrao;
        decimal LerDecimal(string chave, decimal padrao) =>
            decimal.TryParse(LerTexto(chave, ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : padrao;

        return new ConfiguracaoLoja(
            Cep.Normalizar(LerTexto(ChaveCepOrigem, "")),
            Math.Max(0, LerInteiro(ChaveDiasManuseio, 1)),
            Math.Max(0, LerDecimal(ChaveFreteGratis, 0)),
            Math.Clamp(LerInteiro(ChaveParcelasMaximas, 12), 1, 12),
            Math.Max(0, LerDecimal(ChaveParcelaMinima, 10m)));
    }

    public async Task<Resultado> SalvarAsync(ConfiguracaoLoja c, string autor, CancellationToken ct = default)
    {
        if (!Cep.EhValido(c.CepOrigem)) return Resultado.Falha("CEP de origem inválido.");
        if (c.DiasManuseio is < 0 or > 30) return Resultado.Falha("Dias de manuseio deve estar entre 0 e 30.");
        if (c.FreteGratisAPartirDe < 0) return Resultado.Falha("O valor para frete grátis não pode ser negativo.");
        if (c.ParcelasMaximas is < 1 or > 12) return Resultado.Falha("Parcelas máximas deve estar entre 1 e 12.");
        if (c.ParcelaMinima < 0) return Resultado.Falha("A parcela mínima não pode ser negativa.");

        var novos = new Dictionary<string, string>
        {
            [ChaveCepOrigem] = Cep.Normalizar(c.CepOrigem),
            [ChaveDiasManuseio] = c.DiasManuseio.ToString(CultureInfo.InvariantCulture),
            [ChaveFreteGratis] = c.FreteGratisAPartirDe.ToString("0.00", CultureInfo.InvariantCulture),
            [ChaveParcelasMaximas] = c.ParcelasMaximas.ToString(CultureInfo.InvariantCulture),
            [ChaveParcelaMinima] = c.ParcelaMinima.ToString("0.00", CultureInfo.InvariantCulture)
        };

        var existentes = await _db.Configuracoes.ToDictionaryAsync(x => x.Chave, ct);
        foreach (var (chave, valor) in novos)
        {
            if (existentes.TryGetValue(chave, out var atual))
            {
                if (atual.Valor == valor) continue;
                atual.Valor = valor;
                atual.AlteradoEm = _relogio.AgoraUtc;
                atual.AlteradoPor = autor;
            }
            else
            {
                _db.Configuracoes.Add(new Configuracao { Chave = chave, Valor = valor, AlteradoEm = _relogio.AgoraUtc, AlteradoPor = autor });
            }
        }
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }
}
