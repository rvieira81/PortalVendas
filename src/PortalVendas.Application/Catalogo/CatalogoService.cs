using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Catalogo;

namespace PortalVendas.Application.Catalogo;

public enum OrdemVitrine
{
    Recentes,
    MenorPreco,
    MaiorPreco,
    Nome
}

public record FiltroVitrine(
    string? Termo = null,
    string? Categoria = null,
    decimal? PrecoMin = null,
    decimal? PrecoMax = null,
    OrdemVitrine Ordem = OrdemVitrine.Recentes,
    int Pagina = 1,
    int TamanhoPagina = 12);

public record ProdutoResumo(int Id, string Nome, string Slug, decimal Preco, decimal? PrecoDe, int Disponivel, string? Imagem)
{
    public bool Esgotado => Disponivel <= 0;
}

public record CategoriaResumo(int Id, string Nome, string Slug);

public record ProdutoDetalhe(
    int Id, string Nome, string Slug, string Sku, string Descricao, decimal Preco, decimal? PrecoDe,
    int Disponivel, int QuantidadeMaximaCompra, decimal PesoKg, decimal AlturaCm, decimal LarguraCm, decimal ComprimentoCm,
    CategoriaResumo Categoria, IReadOnlyList<string> Imagens)
{
    public bool Esgotado => Disponivel <= 0;
}

public class CatalogoService
{
    public const int TamanhoMaximoPagina = 48;
    private readonly IAppDbContext _db;

    public CatalogoService(IAppDbContext db) => _db = db;

    /// <summary>RF01 e RF02: vitrine paginada com busca, filtros e ordenação.</summary>
    public async Task<Pagina<ProdutoResumo>> ListarVitrineAsync(FiltroVitrine filtro, CancellationToken ct = default)
    {
        var pagina = Pagina<ProdutoResumo>.Normalizar(filtro.Pagina);
        var tamanho = Math.Clamp(filtro.TamanhoPagina, 1, TamanhoMaximoPagina);

        IQueryable<Produto> consulta = _db.Produtos.AsNoTracking().Where(p => p.Ativo && p.Categoria!.Ativa);

        var termo = filtro.Termo?.Trim();
        if (!string.IsNullOrEmpty(termo))
        {
            if (termo.Length > 100) termo = termo[..100];
            // Collation CI_AI no banco: sem diferenciar maiúsculas nem acentos
            consulta = consulta.Where(p => p.Nome.Contains(termo) || p.Descricao.Contains(termo) || p.Sku == termo);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Categoria))
            consulta = consulta.Where(p => p.Categoria!.Slug == filtro.Categoria);

        if (filtro.PrecoMin is > 0m)
            consulta = consulta.Where(p => p.Preco >= filtro.PrecoMin.Value);
        if (filtro.PrecoMax is > 0m)
            consulta = consulta.Where(p => p.Preco <= filtro.PrecoMax.Value);

        var total = await consulta.CountAsync(ct);

        consulta = filtro.Ordem switch
        {
            OrdemVitrine.MenorPreco => consulta.OrderBy(p => p.Preco).ThenBy(p => p.Id),
            OrdemVitrine.MaiorPreco => consulta.OrderByDescending(p => p.Preco).ThenBy(p => p.Id),
            OrdemVitrine.Nome => consulta.OrderBy(p => p.Nome).ThenBy(p => p.Id),
            _ => consulta.OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id)
        };

        var itens = await consulta
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(p => new ProdutoResumo(
                p.Id, p.Nome, p.Slug, p.Preco, p.PrecoDe,
                p.Estoque - p.EstoqueReservado,
                p.Imagens.OrderBy(i => i.Ordem).Select(i => i.Arquivo).FirstOrDefault()))
            .ToListAsync(ct);

        return new Pagina<ProdutoResumo>(itens, pagina, tamanho, total);
    }

    /// <summary>RF03: página do produto. Null para inexistente ou inativo (a tela devolve 404).</summary>
    public async Task<ProdutoDetalhe?> ObterPorSlugAsync(string slug, CancellationToken ct = default)
    {
        var p = await _db.Produtos.AsNoTracking()
            .Include(x => x.Categoria)
            .Include(x => x.Imagens)
            .FirstOrDefaultAsync(x => x.Slug == slug && x.Ativo && x.Categoria!.Ativa, ct);

        if (p is null) return null;

        return new ProdutoDetalhe(
            p.Id, p.Nome, p.Slug, p.Sku, p.Descricao, p.Preco, p.PrecoDe, p.Disponivel, p.QuantidadeMaximaCompra,
            p.PesoKg, p.AlturaCm, p.LarguraCm, p.ComprimentoCm,
            new CategoriaResumo(p.Categoria!.Id, p.Categoria.Nome, p.Categoria.Slug),
            p.Imagens.OrderBy(i => i.Ordem).Select(i => i.Arquivo).ToList());
    }

    public Task<List<CategoriaResumo>> ListarCategoriasAsync(CancellationToken ct = default) =>
        _db.Categorias.AsNoTracking()
            .Where(c => c.Ativa && c.Produtos.Any(p => p.Ativo))
            .OrderBy(c => c.Nome)
            .Select(c => new CategoriaResumo(c.Id, c.Nome, c.Slug))
            .ToListAsync(ct);
}
