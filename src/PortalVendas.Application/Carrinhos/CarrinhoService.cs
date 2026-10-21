using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Carrinhos;

namespace PortalVendas.Application.Carrinhos;

/// <summary>Como o carrinho é identificado: pelo cookie (anônimo) ou pela conta.</summary>
public record IdentificacaoCarrinho(Guid? Token, int? UsuarioId);

public record ItemCarrinhoView(
    int ProdutoId, string Nome, string Slug, string? Imagem, decimal PrecoAtual, decimal PrecoNaInclusao,
    int Quantidade, int QuantidadeMaxima, bool Indisponivel)
{
    public decimal Total => PrecoAtual * Quantidade;
    public bool PrecoMudou => PrecoAtual != PrecoNaInclusao;
}

public record CarrinhoView(Guid Token, IReadOnlyList<ItemCarrinhoView> Itens, IReadOnlyList<string> Avisos)
{
    public decimal Subtotal => Itens.Where(i => !i.Indisponivel).Sum(i => i.Total);
    public int QuantidadeTotal => Itens.Sum(i => i.Quantidade);
    public bool EstaVazio => Itens.Count == 0;
    public bool PodeFinalizar => Itens.Count > 0 && Itens.All(i => !i.Indisponivel);
}

public class CarrinhoService
{
    private readonly IAppDbContext _db;
    private readonly IRelogio _relogio;

    public CarrinhoService(IAppDbContext db, IRelogio relogio)
    {
        _db = db;
        _relogio = relogio;
    }

    public async Task<Carrinho?> ObterAsync(IdentificacaoCarrinho id, CancellationToken ct = default)
    {
        var consulta = _db.Carrinhos.Include(c => c.Itens).ThenInclude(i => i.Produto);
        if (id.UsuarioId is not null)
            return await consulta.FirstOrDefaultAsync(c => c.UsuarioId == id.UsuarioId, ct);
        if (id.Token is not null)
            return await consulta.FirstOrDefaultAsync(c => c.Token == id.Token && c.UsuarioId == null, ct);
        return null;
    }

    private async Task<Carrinho> ObterOuCriarAsync(IdentificacaoCarrinho id, CancellationToken ct)
    {
        var carrinho = await ObterAsync(id, ct);
        if (carrinho is not null) return carrinho;

        var agora = _relogio.AgoraUtc;
        carrinho = new Carrinho { UsuarioId = id.UsuarioId, CriadoEm = agora, AtualizadoEm = agora };
        _db.Carrinhos.Add(carrinho);
        return carrinho;
    }

    /// <summary>RF04: adiciona ao carrinho limitando ao disponível. Devolve o token do carrinho (para o cookie).</summary>
    public async Task<Resultado<Guid>> AdicionarAsync(IdentificacaoCarrinho id, int produtoId, int quantidade, CancellationToken ct = default)
    {
        if (quantidade is < 1 or > Domain.Catalogo.Produto.QuantidadeMaximaPorItem)
            return Resultado<Guid>.Falha("Quantidade inválida.");

        var produto = await _db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId && p.Ativo, ct);
        if (produto is null) return Resultado<Guid>.Falha("Produto não encontrado.");
        if (produto.Disponivel <= 0) return Resultado<Guid>.Falha("Produto esgotado.");

        var carrinho = await ObterOuCriarAsync(id, ct);
        var (_, limitado) = carrinho.Adicionar(produto, quantidade, _relogio.AgoraUtc);
        await _db.SaveChangesAsync(ct);

        return limitado
            ? new Resultado<Guid>(true, $"Quantidade indisponível. Ajustamos para o máximo disponível de \"{produto.Nome}\".", carrinho.Token)
            : Resultado<Guid>.Ok(carrinho.Token);
    }

    public async Task<Resultado> AlterarQuantidadeAsync(IdentificacaoCarrinho id, int produtoId, int quantidade, CancellationToken ct = default)
    {
        var carrinho = await ObterAsync(id, ct);
        var item = carrinho?.Itens.FirstOrDefault(i => i.ProdutoId == produtoId);
        if (carrinho is null || item?.Produto is null) return Resultado.Falha("Item não encontrado no carrinho.");

        var (_, limitado) = carrinho.DefinirQuantidade(item.Produto, Math.Min(quantidade, Domain.Catalogo.Produto.QuantidadeMaximaPorItem), _relogio.AgoraUtc);
        await _db.SaveChangesAsync(ct);
        return limitado ? Resultado.Falha("Quantidade indisponível. Ajustamos para o máximo disponível.") : Resultado.Ok();
    }

    public async Task RemoverAsync(IdentificacaoCarrinho id, int produtoId, CancellationToken ct = default)
    {
        var carrinho = await ObterAsync(id, ct);
        if (carrinho is null) return;
        var item = carrinho.Itens.FirstOrDefault(i => i.ProdutoId == produtoId);
        if (item is null) return;
        _db.CarrinhoItens.Remove(item);
        carrinho.Remover(produtoId, _relogio.AgoraUtc);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>RF04: no login, junta o carrinho anônimo ao da conta e descarta o anônimo.</summary>
    public async Task MesclarAoEntrarAsync(Guid? tokenAnonimo, int usuarioId, CancellationToken ct = default)
    {
        if (tokenAnonimo is null) return;
        var anonimo = await ObterAsync(new IdentificacaoCarrinho(tokenAnonimo, null), ct);
        if (anonimo is null) return;

        var agora = _relogio.AgoraUtc;
        var daConta = await ObterAsync(new IdentificacaoCarrinho(null, usuarioId), ct);
        if (daConta is null)
        {
            // Sem carrinho na conta: o anônimo passa a ser da conta
            anonimo.UsuarioId = usuarioId;
            anonimo.AtualizadoEm = agora;
        }
        else
        {
            var produtos = anonimo.Itens.Where(i => i.Produto is not null).ToDictionary(i => i.ProdutoId, i => i.Produto!);
            daConta.Mesclar(anonimo, produtos, agora);
            _db.Carrinhos.Remove(anonimo);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> ContarItensAsync(IdentificacaoCarrinho id, CancellationToken ct = default)
    {
        IQueryable<Carrinho> consulta = _db.Carrinhos.AsNoTracking();
        if (id.UsuarioId is not null) consulta = consulta.Where(c => c.UsuarioId == id.UsuarioId);
        else if (id.Token is not null) consulta = consulta.Where(c => c.Token == id.Token && c.UsuarioId == null);
        else return 0;
        return await consulta.SelectMany(c => c.Itens).SumAsync(i => (int?)i.Quantidade, ct) ?? 0;
    }

    /// <summary>Visão do carrinho com preço atual, disponibilidade e avisos (RF04).</summary>
    public async Task<CarrinhoView?> MontarViewAsync(IdentificacaoCarrinho id, CancellationToken ct = default)
    {
        var carrinho = await ObterAsync(id, ct);
        if (carrinho is null) return null;

        var produtoIds = carrinho.Itens.Select(i => i.ProdutoId).ToList();
        var imagens = await _db.ProdutoImagens.AsNoTracking()
            .Where(i => produtoIds.Contains(i.ProdutoId))
            .OrderBy(i => i.Ordem)
            .Select(i => new { i.ProdutoId, i.Arquivo })
            .ToListAsync(ct);
        var imagemPorProduto = imagens.GroupBy(i => i.ProdutoId).ToDictionary(g => g.Key, g => g.First().Arquivo);

        var avisos = new List<string>();
        var itens = new List<ItemCarrinhoView>();
        foreach (var item in carrinho.Itens.OrderBy(i => i.Id))
        {
            var p = item.Produto!;
            var indisponivel = !p.Ativo || p.Disponivel <= 0;
            if (indisponivel)
                avisos.Add($"\"{p.Nome}\" não está mais disponível. Remova-o para continuar.");
            else if (item.Quantidade > p.Disponivel)
                avisos.Add($"Só há {p.Disponivel} unidade(s) de \"{p.Nome}\". Ajuste a quantidade.");
            if (!indisponivel && p.Preco != item.PrecoNaInclusao)
                avisos.Add($"O preço de \"{p.Nome}\" mudou de {item.PrecoNaInclusao:C} para {p.Preco:C}.");

            itens.Add(new ItemCarrinhoView(
                p.Id, p.Nome, p.Slug, imagemPorProduto.GetValueOrDefault(p.Id), p.Preco, item.PrecoNaInclusao,
                item.Quantidade, Math.Max(1, p.QuantidadeMaximaCompra),
                indisponivel || item.Quantidade > p.Disponivel));
        }
        return new CarrinhoView(carrinho.Token, itens, avisos);
    }
}
