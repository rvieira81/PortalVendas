using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Catalogo;
using PortalVendas.Domain.Comum;

namespace PortalVendas.Application.Admin;

public record ProdutoAdminResumo(int Id, string Sku, string Nome, string Categoria, decimal Preco, int Estoque, int Reservado, bool Ativo, string? Imagem);

public record DadosProduto(
    int Id, int CategoriaId, string Sku, string Nome, string? Descricao, decimal Preco, decimal? PrecoDe, int Estoque,
    decimal PesoKg, decimal AlturaCm, decimal LarguraCm, decimal ComprimentoCm, bool Ativo);

public class ProdutoAdminService
{
    public const int TamanhoPagina = 25;
    public const int MaximoImagens = 8;

    private readonly IAppDbContext _db;
    private readonly IArmazenamentoImagens _imagens;
    private readonly IRelogio _relogio;
    private readonly ILogger<ProdutoAdminService> _logger;

    public ProdutoAdminService(IAppDbContext db, IArmazenamentoImagens imagens, IRelogio relogio, ILogger<ProdutoAdminService> logger)
    {
        _db = db;
        _imagens = imagens;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task<Pagina<ProdutoAdminResumo>> ListarAsync(string? termo, int? categoriaId, int pagina, CancellationToken ct = default)
    {
        pagina = Pagina<ProdutoAdminResumo>.Normalizar(pagina);
        var consulta = _db.Produtos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            var t = termo.Trim();
            consulta = consulta.Where(p => p.Nome.Contains(t) || p.Sku.Contains(t));
        }
        if (categoriaId is > 0) consulta = consulta.Where(p => p.CategoriaId == categoriaId);

        var total = await consulta.CountAsync(ct);
        var itens = await consulta
            .OrderBy(p => p.Nome).ThenBy(p => p.Id)
            .Skip((pagina - 1) * TamanhoPagina).Take(TamanhoPagina)
            .Select(p => new ProdutoAdminResumo(p.Id, p.Sku, p.Nome, p.Categoria!.Nome, p.Preco, p.Estoque, p.EstoqueReservado, p.Ativo,
                p.Imagens.OrderBy(i => i.Ordem).Select(i => i.Arquivo).FirstOrDefault()))
            .ToListAsync(ct);
        return new Pagina<ProdutoAdminResumo>(itens, pagina, TamanhoPagina, total);
    }

    public Task<Produto?> ObterAsync(int id, CancellationToken ct = default) =>
        _db.Produtos.AsNoTracking().Include(p => p.Imagens.OrderBy(i => i.Ordem)).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<List<Domain.Catalogo.Categoria>> ListarCategoriasAsync(CancellationToken ct = default) =>
        _db.Categorias.AsNoTracking().OrderBy(c => c.Nome).ToListAsync(ct);

    public async Task<Resultado<int>> SalvarAsync(DadosProduto d, string autor, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;
        Produto produto;
        if (d.Id == 0)
        {
            produto = new Produto { CriadoEm = agora, CriadoPor = autor };
        }
        else
        {
            var existente = await _db.Produtos.FirstOrDefaultAsync(p => p.Id == d.Id, ct);
            if (existente is null) return Resultado<int>.Falha("Produto não encontrado.");
            produto = existente;
            produto.AlteradoEm = agora;
            produto.AlteradoPor = autor;
        }

        produto.CategoriaId = d.CategoriaId;
        produto.Sku = d.Sku ?? string.Empty;
        produto.Nome = d.Nome ?? string.Empty;
        produto.Descricao = d.Descricao ?? string.Empty;
        produto.Preco = Math.Round(d.Preco, 2);
        produto.PrecoDe = d.PrecoDe is > 0m ? Math.Round(d.PrecoDe.Value, 2) : null;
        produto.Estoque = d.Estoque;
        produto.PesoKg = Math.Round(d.PesoKg, 3);
        produto.AlturaCm = Math.Round(d.AlturaCm, 1);
        produto.LarguraCm = Math.Round(d.LarguraCm, 1);
        produto.ComprimentoCm = Math.Round(d.ComprimentoCm, 1);
        produto.Ativo = d.Ativo;
        produto.NormalizarCampos();

        var erros = produto.Validar().ToList();
        if (!await _db.Categorias.AnyAsync(c => c.Id == d.CategoriaId, ct)) erros.Add("Categoria inválida.");
        if (await _db.Produtos.AnyAsync(p => p.Sku == produto.Sku && p.Id != produto.Id, ct)) erros.Add("Já existe um produto com este SKU.");
        if (erros.Count > 0)
        {
            _db.ChangeTracker.Clear();
            return Resultado<int>.Falha(string.Join(" ", erros));
        }

        if (d.Id == 0)
        {
            produto.Slug = await GerarSlugUnicoAsync(produto.Nome, null, ct);
            _db.Produtos.Add(produto);
        }
        else if (string.IsNullOrEmpty(produto.Slug))
        {
            produto.Slug = await GerarSlugUnicoAsync(produto.Nome, produto.Id, ct);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Erro ao salvar produto {Sku}", d.Sku);
            // A CHECK do banco barra estoque menor que o reservado mesmo em corrida com uma compra
            return Resultado<int>.Falha("Não foi possível salvar. Confira se o estoque não ficou menor que o reservado e tente de novo.");
        }
        return Resultado<int>.Ok(produto.Id);
    }

    private async Task<string> GerarSlugUnicoAsync(string nome, int? idAtual, CancellationToken ct)
    {
        var baseSlug = Texto.GerarSlug(nome);
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "produto";
        var slug = baseSlug;
        for (var i = 2; await _db.Produtos.AnyAsync(p => p.Slug == slug && p.Id != (idAtual ?? 0), ct); i++)
            slug = $"{baseSlug}-{i}";
        return slug;
    }

    public async Task<Resultado> AdicionarImagemAsync(int produtoId, byte[] conteudo, string nomeOriginal, CancellationToken ct = default)
    {
        var erro = ValidadorImagem.Validar(conteudo, nomeOriginal, out var extensao);
        if (erro is not null) return Resultado.Falha(erro);

        var produto = await _db.Produtos.Include(p => p.Imagens).FirstOrDefaultAsync(p => p.Id == produtoId, ct);
        if (produto is null) return Resultado.Falha("Produto não encontrado.");
        if (produto.Imagens.Count >= MaximoImagens) return Resultado.Falha($"Limite de {MaximoImagens} imagens por produto.");

        var arquivo = await _imagens.SalvarAsync(conteudo, extensao!, ct);
        produto.Imagens.Add(new ProdutoImagem
        {
            Arquivo = arquivo,
            Ordem = produto.Imagens.Count == 0 ? 0 : produto.Imagens.Max(i => i.Ordem) + 1
        });
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> RemoverImagemAsync(int produtoId, int imagemId, CancellationToken ct = default)
    {
        var imagem = await _db.ProdutoImagens.FirstOrDefaultAsync(i => i.Id == imagemId && i.ProdutoId == produtoId, ct);
        if (imagem is null) return Resultado.Falha("Imagem não encontrada.");
        _db.ProdutoImagens.Remove(imagem);
        await _db.SaveChangesAsync(ct);
        await _imagens.RemoverAsync(imagem.Arquivo, ct);
        return Resultado.Ok();
    }

    /// <summary>Move a imagem para a primeira posição (vira a foto principal da vitrine).</summary>
    public async Task<Resultado> DefinirImagemPrincipalAsync(int produtoId, int imagemId, CancellationToken ct = default)
    {
        var imagens = await _db.ProdutoImagens.Where(i => i.ProdutoId == produtoId).OrderBy(i => i.Ordem).ToListAsync(ct);
        var principal = imagens.FirstOrDefault(i => i.Id == imagemId);
        if (principal is null) return Resultado.Falha("Imagem não encontrada.");
        var ordem = 1;
        foreach (var i in imagens) i.Ordem = ReferenceEquals(i, principal) ? 0 : ordem++;
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>RF17: produto que já vendeu é só desativado, para preservar o histórico.</summary>
    public async Task<Resultado<bool>> ExcluirAsync(int produtoId, string autor, CancellationToken ct = default)
    {
        var produto = await _db.Produtos.Include(p => p.Imagens).FirstOrDefaultAsync(p => p.Id == produtoId, ct);
        if (produto is null) return Resultado<bool>.Falha("Produto não encontrado.");

        var teveVendas = await _db.PedidoItens.AnyAsync(i => i.ProdutoId == produtoId, ct);
        if (teveVendas)
        {
            produto.Ativo = false;
            produto.AlteradoEm = _relogio.AgoraUtc;
            produto.AlteradoPor = autor;
            await _db.SaveChangesAsync(ct);
            return Resultado<bool>.Ok(false);   // desativado
        }

        var arquivos = produto.Imagens.Select(i => i.Arquivo).ToList();
        await _db.CarrinhoItens.Where(i => i.ProdutoId == produtoId).ExecuteDeleteAsync(ct);
        _db.Produtos.Remove(produto);
        await _db.SaveChangesAsync(ct);
        foreach (var a in arquivos) await _imagens.RemoverAsync(a, ct);
        return Resultado<bool>.Ok(true);   // excluído
    }
}
