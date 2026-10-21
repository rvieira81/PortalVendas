using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Carrinhos;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Emails;
using PortalVendas.Application.Frete;
using PortalVendas.Domain.Carrinhos;
using PortalVendas.Domain.Pedidos;

namespace PortalVendas.Application.Pedidos;

public record ComandoCriarPedido(int UsuarioId, Guid ChaveIdempotencia, int EnderecoId, string CodigoFrete);

public class CheckoutService
{
    private readonly IAppDbContext _db;
    private readonly CarrinhoService _carrinhos;
    private readonly FreteService _frete;
    private readonly EstoqueService _estoque;
    private readonly NotificadorPedidos _notificador;
    private readonly IRelogio _relogio;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(IAppDbContext db, CarrinhoService carrinhos, FreteService frete, EstoqueService estoque,
        NotificadorPedidos notificador, IRelogio relogio, ILogger<CheckoutService> logger)
    {
        _db = db;
        _carrinhos = carrinhos;
        _frete = frete;
        _estoque = estoque;
        _notificador = notificador;
        _relogio = relogio;
        _logger = logger;
    }

    public static IReadOnlyList<ItemFrete> ItensParaFrete(Carrinho carrinho) =>
        carrinho.Itens
            .Where(i => i.Produto is not null)
            .Select(i => new ItemFrete(i.ProdutoId, i.Produto!.PesoKg, i.Produto.AlturaCm, i.Produto.LarguraCm,
                i.Produto.ComprimentoCm, i.Produto.Preco, i.Quantidade))
            .ToList();

    /// <summary>RF08: cota o frete do carrinho do cliente para um CEP.</summary>
    public async Task<Resultado<IReadOnlyList<OpcaoFrete>>> CotarFreteAsync(int usuarioId, string cep, CancellationToken ct = default)
    {
        var carrinho = await _carrinhos.ObterAsync(new IdentificacaoCarrinho(null, usuarioId), ct);
        if (carrinho is null || carrinho.EstaVazio) return Resultado<IReadOnlyList<OpcaoFrete>>.Falha("O carrinho está vazio.");
        return await _frete.CotarAsync(cep, ItensParaFrete(carrinho), ct);
    }

    /// <summary>
    /// RF09: cria o pedido a partir do carrinho. Recalcula preços e frete no servidor, reserva o estoque
    /// de forma atômica e é idempotente pela chave gerada no formulário.
    /// </summary>
    public async Task<Resultado<int>> CriarPedidoAsync(ComandoCriarPedido cmd, CancellationToken ct = default)
    {
        var existente = await BuscarPorChaveAsync(cmd, ct);
        if (existente is not null) return Resultado<int>.Ok(existente.Value);

        var usuario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == cmd.UsuarioId, ct);
        if (usuario is null || usuario.Anonimizado) return Resultado<int>.Falha("Conta não encontrada.");
        if (!usuario.EmailConfirmado) return Resultado<int>.Falha("Confirme seu e-mail antes de finalizar a compra.");
        if (string.IsNullOrEmpty(usuario.Cpf)) return Resultado<int>.Falha("Informe seu CPF em Minha conta antes de finalizar a compra.");

        var endereco = await _db.Enderecos.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == cmd.EnderecoId && e.UsuarioId == cmd.UsuarioId, ct);
        if (endereco is null) return Resultado<int>.Falha("Escolha um endereço de entrega.");

        var carrinho = await _carrinhos.ObterAsync(new IdentificacaoCarrinho(null, cmd.UsuarioId), ct);
        if (carrinho is null || carrinho.EstaVazio) return Resultado<int>.Falha("O carrinho está vazio.");

        var indisponiveis = carrinho.Itens
            .Where(i => i.Produto is null || !i.Produto.Ativo || i.Produto.Disponivel < i.Quantidade)
            .Select(i => i.Produto?.Nome ?? $"Produto {i.ProdutoId}")
            .ToList();
        if (indisponiveis.Count > 0)
            return Resultado<int>.Falha($"Sem estoque suficiente para: {string.Join(", ", indisponiveis)}. Revise o carrinho.");

        // Frete recotado no servidor: o valor nunca vem do navegador
        var cotacao = await _frete.CotarAsync(endereco.Cep, ItensParaFrete(carrinho), ct);
        if (!cotacao.Sucesso) return Resultado<int>.Falha(cotacao.Erro!);
        var frete = cotacao.Valor!.FirstOrDefault(o => o.Codigo == cmd.CodigoFrete);
        if (frete is null)
            return Resultado<int>.Falha("A opção de frete escolhida não está mais disponível. Escolha novamente.");

        var agora = _relogio.AgoraUtc;
        var pedido = Pedido.Criar(
            cmd.UsuarioId,
            cmd.ChaveIdempotencia,
            carrinho.Itens.Select(i => (i.ProdutoId, i.Produto!.Nome, i.Produto.Sku, i.Produto.Preco, i.Quantidade)),
            EnderecoEntrega.De(endereco),
            frete.ServicoId,
            frete.Descricao.Length > 150 ? frete.Descricao[..150] : frete.Descricao,
            frete.Valor,
            frete.PrazoDias,
            desconto: 0m,
            agora,
            Autores.Cliente(cmd.UsuarioId));

        var estrategia = _db.Database.CreateExecutionStrategy();
        Resultado<int> resultado;
        try
        {
            resultado = await estrategia.ExecuteAsync(async () =>
            {
                await using var transacao = await _db.Database.BeginTransactionAsync(ct);

                foreach (var item in pedido.Itens)
                {
                    if (!await _estoque.ReservarAsync(item.ProdutoId, item.Quantidade, ct))
                    {
                        await transacao.RollbackAsync(ct);
                        // RF09: outro cliente levou a última unidade enquanto este estava no checkout
                        return Resultado<int>.Falha($"\"{item.NomeProduto}\" acabou de esgotar. Revise o carrinho.");
                    }
                }

                _db.Pedidos.Add(pedido);
                _db.CarrinhoItens.RemoveRange(carrinho.Itens);
                carrinho.AtualizadoEm = agora;
                await _db.SaveChangesAsync(ct);
                await transacao.CommitAsync(ct);
                return Resultado<int>.Ok(pedido.Id);
            });
        }
        catch (DbUpdateException ex)
        {
            // Duplo clique em paralelo: a UNIQUE da chave de idempotência barrou o segundo pedido
            _db.ChangeTracker.Clear();
            var duplicado = await BuscarPorChaveAsync(cmd, ct);
            if (duplicado is not null) return Resultado<int>.Ok(duplicado.Value);
            _logger.LogError(ex, "Erro ao gravar pedido do usuário {UsuarioId}", cmd.UsuarioId);
            return Resultado<int>.Falha("Não foi possível registrar o pedido. Tente novamente.");
        }

        if (resultado.Sucesso)
        {
            _logger.LogInformation("Pedido {PedidoId} criado: total {Total}, frete {Frete} ({Servico})",
                pedido.Id, pedido.Total, pedido.ValorFrete, pedido.FreteDescricao);
            await _notificador.NotificarStatusAsync(pedido.Id, ct);
        }
        else
        {
            _db.ChangeTracker.Clear();
        }
        return resultado;
    }

    private async Task<int?> BuscarPorChaveAsync(ComandoCriarPedido cmd, CancellationToken ct) =>
        await _db.Pedidos.AsNoTracking()
            .Where(p => p.ChaveIdempotencia == cmd.ChaveIdempotencia && p.UsuarioId == cmd.UsuarioId)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
}
