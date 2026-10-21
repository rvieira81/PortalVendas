using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Comum;

namespace PortalVendas.Application.Frete;

public record ItemFrete(int ProdutoId, decimal PesoKg, decimal AlturaCm, decimal LarguraCm, decimal ComprimentoCm, decimal ValorUnitario, int Quantidade);

public record SolicitacaoFrete(string CepOrigem, string CepDestino, IReadOnlyList<ItemFrete> Itens);

/// <summary>Opção vinda do provedor (antes de manuseio e frete grátis).</summary>
public record CotacaoProvedor(int ServicoId, string Transportadora, string Servico, decimal Valor, int PrazoDias);

/// <summary>Opção apresentada ao cliente. <see cref="Codigo"/> identifica a opção na confirmação do pedido.</summary>
public record OpcaoFrete(string Codigo, int? ServicoId, string Descricao, decimal Valor, decimal ValorOriginal, int PrazoDias, bool Contingencia)
{
    public bool Gratis => Valor == 0;
}

public interface IProvedorFrete
{
    /// <summary>Cota em tempo real. Lança exceção se o provedor estiver indisponível.</summary>
    Task<IReadOnlyList<CotacaoProvedor>> CotarAsync(SolicitacaoFrete solicitacao, CancellationToken ct = default);
}

public class FreteService
{
    private readonly IAppDbContext _db;
    private readonly IProvedorFrete _provedor;
    private readonly ConfiguracaoLojaService _configuracao;
    private readonly ILogger<FreteService> _logger;

    public FreteService(IAppDbContext db, IProvedorFrete provedor, ConfiguracaoLojaService configuracao, ILogger<FreteService> logger)
    {
        _db = db;
        _provedor = provedor;
        _configuracao = configuracao;
        _logger = logger;
    }

    /// <summary>
    /// RF08: cota no provedor (Melhor Envio) por peso e dimensões; se falhar, usa a tabela de contingência.
    /// Soma os dias de manuseio e aplica frete grátis na opção mais barata quando o subtotal atinge o mínimo.
    /// </summary>
    public async Task<Resultado<IReadOnlyList<OpcaoFrete>>> CotarAsync(string cepDestino, IReadOnlyList<ItemFrete> itens, CancellationToken ct = default)
    {
        var cep = Cep.Normalizar(cepDestino);
        if (!Cep.EhValido(cep)) return Resultado<IReadOnlyList<OpcaoFrete>>.Falha("CEP inválido.");
        if (itens.Count == 0) return Resultado<IReadOnlyList<OpcaoFrete>>.Falha("O carrinho está vazio.");

        var config = await _configuracao.ObterAsync(ct);
        var subtotal = itens.Sum(i => i.ValorUnitario * i.Quantidade);
        var opcoes = new List<OpcaoFrete>();

        if (Cep.EhValido(config.CepOrigem))
        {
            try
            {
                var cotacoes = await _provedor.CotarAsync(new SolicitacaoFrete(config.CepOrigem, cep, itens), ct);
                opcoes.AddRange(cotacoes
                    .Where(c => c.Valor >= 0 && c.PrazoDias > 0)
                    .Select(c => new OpcaoFrete(
                        $"ME-{c.ServicoId}", c.ServicoId, $"{c.Transportadora} — {c.Servico}",
                        Math.Round(c.Valor, 2), Math.Round(c.Valor, 2), c.PrazoDias + config.DiasManuseio, false)));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cotação de frete em tempo real indisponível; usando a tabela de contingência");
            }
        }
        else
        {
            _logger.LogWarning("CEP de origem não configurado; usando a tabela de contingência de frete");
        }

        if (opcoes.Count == 0)
        {
            var faixas = await _db.FaixasFrete.AsNoTracking().Where(f => f.Ativa).ToListAsync(ct);
            var faixa = faixas
                .Where(f => f.Atende(cep))
                .OrderBy(f => long.Parse(f.CepFim) - long.Parse(f.CepInicio))   // a faixa mais específica vence
                .ThenBy(f => f.Valor)
                .FirstOrDefault();
            if (faixa is not null)
                opcoes.Add(new OpcaoFrete($"CT-{faixa.Id}", null, $"Entrega padrão ({faixa.Descricao})",
                    faixa.Valor, faixa.Valor, faixa.PrazoDias + config.DiasManuseio, true));
        }

        if (opcoes.Count == 0)
            return Resultado<IReadOnlyList<OpcaoFrete>>.Falha("Não entregamos neste CEP.");

        opcoes = opcoes.OrderBy(o => o.Valor).ThenBy(o => o.PrazoDias).ToList();

        if (config.TemFreteGratis && subtotal >= config.FreteGratisAPartirDe)
            opcoes[0] = opcoes[0] with { Valor = 0m, Descricao = opcoes[0].Descricao + " — frete grátis" };

        return Resultado<IReadOnlyList<OpcaoFrete>>.Ok(opcoes);
    }
}
