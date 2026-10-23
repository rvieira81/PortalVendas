using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Frete;
using PortalVendas.Domain.Catalogo;
using PortalVendas.Domain.Pedidos;

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

public record CategoriaComFoto(int Id, string Nome, string Slug, string? Imagem, int Produtos);

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

    // ------------------------------------------------------------------ vitrine da página inicial

    private IQueryable<Produto> Vendaveis() =>
        _db.Produtos.AsNoTracking().Where(p => p.Ativo && p.Categoria!.Ativa && p.Estoque - p.EstoqueReservado > 0);

    private static IQueryable<ProdutoResumo> ParaResumo(IQueryable<Produto> consulta) =>
        consulta.Select(p => new ProdutoResumo(
            p.Id, p.Nome, p.Slug, p.Preco, p.PrecoDe,
            p.Estoque - p.EstoqueReservado,
            p.Imagens.OrderBy(i => i.Ordem).Select(i => i.Arquivo).FirstOrDefault()));

    /// <summary>Produtos com preço "de" (em promoção), do maior para o menor desconto.</summary>
    public Task<List<ProdutoResumo>> ListarOfertasAsync(int quantidade = 10, CancellationToken ct = default) =>
        ParaResumo(Vendaveis()
                .Where(p => p.PrecoDe != null && p.PrecoDe > p.Preco)
                .OrderByDescending(p => (p.PrecoDe!.Value - p.Preco) / p.PrecoDe.Value)
                .ThenBy(p => p.Id)
                .Take(quantidade))
            .ToListAsync(ct);

    public Task<List<ProdutoResumo>> ListarNovidadesAsync(int quantidade = 10, CancellationToken ct = default) =>
        ParaResumo(Vendaveis().OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id).Take(quantidade)).ToListAsync(ct);

    /// <summary>Mais vendidos nos últimos 90 dias (pedidos pagos); completa com novidades quando há poucas vendas.</summary>
    public async Task<List<ProdutoResumo>> ListarMaisVendidosAsync(DateTime agoraUtc, int quantidade = 10, CancellationToken ct = default)
    {
        var desde = agoraUtc.AddDays(-90);
        var pagos = new[] { StatusPedido.Pago, StatusPedido.EmSeparacao, StatusPedido.Enviado, StatusPedido.Entregue };
        var ranking = await (
                from item in _db.PedidoItens.AsNoTracking()
                join pedido in _db.Pedidos.AsNoTracking() on item.PedidoId equals pedido.Id
                where pedido.CriadoEm >= desde && pagos.Contains(pedido.Status)
                group item by item.ProdutoId into g
                select new { ProdutoId = g.Key, Quantidade = g.Sum(x => x.Quantidade) })
            .OrderByDescending(x => x.Quantidade)
            .Take(quantidade * 2)
            .ToListAsync(ct);

        var ids = ranking.Select(r => r.ProdutoId).ToList();
        var vendidos = await ParaResumo(Vendaveis().Where(p => ids.Contains(p.Id))).ToListAsync(ct);
        var resultado = vendidos.OrderBy(p => ids.IndexOf(p.Id)).Take(quantidade).ToList();

        if (resultado.Count < quantidade)
        {
            var jaIncluidos = resultado.Select(p => p.Id).ToList();
            resultado.AddRange(await ParaResumo(Vendaveis()
                    .Where(p => !jaIncluidos.Contains(p.Id))
                    .OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id)
                    .Take(quantidade - resultado.Count))
                .ToListAsync(ct));
        }
        return resultado;
    }

    /// <summary>Categorias com a foto de um produto, para o atalho "Compre por categoria".</summary>
    public Task<List<CategoriaComFoto>> ListarCategoriasComFotoAsync(CancellationToken ct = default) =>
        _db.Categorias.AsNoTracking()
            .Where(c => c.Ativa && c.Produtos.Any(p => p.Ativo))
            .OrderBy(c => c.Nome)
            .Select(c => new CategoriaComFoto(
                c.Id, c.Nome, c.Slug,
                c.Produtos.Where(p => p.Ativo && p.Imagens.Any())
                    .OrderByDescending(p => p.CriadoEm)
                    .Select(p => p.Imagens.OrderBy(i => i.Ordem).Select(i => i.Arquivo).FirstOrDefault())
                    .FirstOrDefault(),
                c.Produtos.Count(p => p.Ativo)))
            .ToListAsync(ct);

    /// <summary>"Quem viu este produto também viu": outros produtos da mesma categoria.</summary>
    public Task<List<ProdutoResumo>> ListarRelacionadosAsync(int categoriaId, int excluirProdutoId, int quantidade = 4, CancellationToken ct = default) =>
        ParaResumo(Vendaveis()
                .Where(p => p.CategoriaId == categoriaId && p.Id != excluirProdutoId)
                .OrderByDescending(p => p.PrecoDe != null).ThenByDescending(p => p.CriadoEm)
                .Take(quantidade))
            .ToListAsync(ct);

    /// <summary>Dados para cotar o frete de um produto na própria página (RF08).</summary>
    public async Task<ItemFrete?> ObterItemFreteAsync(int produtoId, int quantidade, CancellationToken ct = default)
    {
        var p = await _db.Produtos.AsNoTracking()
            .Where(x => x.Id == produtoId && x.Ativo && x.Categoria!.Ativa)
            .Select(x => new { x.Id, x.PesoKg, x.AlturaCm, x.LarguraCm, x.ComprimentoCm, x.Preco })
            .FirstOrDefaultAsync(ct);
        return p is null ? null : new ItemFrete(p.Id, p.PesoKg, p.AlturaCm, p.LarguraCm, p.ComprimentoCm, p.Preco, Math.Clamp(quantidade, 1, 10));
    }
}
