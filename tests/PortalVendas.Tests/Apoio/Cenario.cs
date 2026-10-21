using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Carrinhos;
using PortalVendas.Application.Contas;
using PortalVendas.Application.Emails;
using PortalVendas.Application.Frete;
using PortalVendas.Application.Pagamentos;
using PortalVendas.Application.Pedidos;
using PortalVendas.Domain.Catalogo;
using PortalVendas.Domain.Frete;
using PortalVendas.Domain.Usuarios;
using PortalVendas.Infrastructure.Dados;
using PortalVendas.Infrastructure.Servicos;

namespace PortalVendas.Tests.Apoio;

/// <summary>
/// Banco SQLite em memória com o mesmo mapeamento do EF usado em produção, mais serviços reais
/// e integrações falsas (gateway, frete, e-mail). Cada teste cria o seu cenário.
/// </summary>
public sealed class Cenario : IDisposable
{
    public const string CepOrigem = "13270000";
    public const string CepCliente = "13084000";
    public const string SenhaPadrao = "SenhaForte123";

    private readonly SqliteConnection _conexao;
    private int _sequencia;

    public AppDbContext Db { get; }
    public RelogioFalso Relogio { get; } = new(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc));
    public GatewayFalso Gateway { get; } = new();
    public ProvedorFreteFalso Frete { get; } = new();
    public EmailFalso Email { get; } = new();
    public LinksFalsos Links { get; } = new();
    public HashSenha Hash { get; } = new();
    public ProtetorDados Protetor { get; } = new(new EphemeralDataProtectionProvider());

    public Cenario()
    {
        _conexao = new SqliteConnection("DataSource=:memory:");
        _conexao.Open();
        Db = NovoContexto();
        Db.Database.EnsureCreated();

        Db.Configuracoes.AddRange(
            new Configuracao { Chave = ConfiguracaoLojaService.ChaveCepOrigem, Valor = CepOrigem },
            new Configuracao { Chave = ConfiguracaoLojaService.ChaveDiasManuseio, Valor = "1" },
            new Configuracao { Chave = ConfiguracaoLojaService.ChaveFreteGratis, Valor = "0" },
            new Configuracao { Chave = ConfiguracaoLojaService.ChaveParcelasMaximas, Valor = "12" },
            new Configuracao { Chave = ConfiguracaoLojaService.ChaveParcelaMinima, Valor = "10.00" });
        Db.Categorias.Add(new Categoria { Nome = "Geral", Slug = "geral", CriadoEm = Relogio.AgoraUtc });
        Db.SaveChanges();
    }

    public AppDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conexao).Options);

    // ---------------------------------------------------------------- serviços (mesmo contexto, como um escopo de requisição)

    public ConfiguracaoLojaService Configuracao => new(Db, Relogio);
    public FreteService FreteService => new(Db, Frete, Configuracao, NullLogger<FreteService>.Instance);
    public CarrinhoService Carrinhos => new(Db, Relogio);
    public EstoqueService Estoque => new(Db);
    public NotificadorPedidos Notificador => new(Db, Email, Links, NullLogger<NotificadorPedidos>.Instance);
    public CheckoutService Checkout => new(Db, Carrinhos, FreteService, Estoque, Notificador, Relogio, NullLogger<CheckoutService>.Instance);
    public PagamentoService Pagamentos => new(Db, Gateway, Estoque, Configuracao, Notificador, Relogio, NullLogger<PagamentoService>.Instance);
    public PedidoService Pedidos => new(Db, Gateway, Estoque, Notificador, Relogio, NullLogger<PedidoService>.Instance);
    public ContaService Contas => new(Db, Hash, Protetor, Email, Links, Relogio, NullLogger<ContaService>.Instance);
    public EnderecoService Enderecos => new(Db, Relogio);

    // ---------------------------------------------------------------- dados

    public async Task<Produto> CriarProdutoAsync(int estoque = 10, decimal preco = 100m, string? nome = null)
    {
        var n = ++_sequencia;
        var produto = new Produto
        {
            CategoriaId = Db.Categorias.First().Id,
            Sku = $"SKU-{n:000}",
            Nome = nome ?? $"Produto {n}",
            Slug = $"produto-{n}",
            Descricao = "Descrição de teste",
            Preco = preco,
            Estoque = estoque,
            PesoKg = 0.5m,
            AlturaCm = 10,
            LarguraCm = 15,
            ComprimentoCm = 20,
            Ativo = true,
            CriadoEm = Relogio.AgoraUtc,
            CriadoPor = "teste"
        };
        Db.Produtos.Add(produto);
        await Db.SaveChangesAsync();
        return produto;
    }

    public async Task<Usuario> CriarClienteAsync(bool emailConfirmado = true, string? email = null, string cpf = "52998224725")
    {
        var n = ++_sequencia;
        var usuario = new Usuario
        {
            Nome = "Maria da Silva",
            Email = email ?? $"cliente{n}@teste.com.br",
            EmailConfirmado = emailConfirmado,
            Cpf = cpf,
            Telefone = "19999990000",
            Perfil = PerfilUsuario.Cliente,
            SenhaHash = Hash.Gerar(SenhaPadrao),
            AceitouTermosEm = Relogio.AgoraUtc,
            CriadoEm = Relogio.AgoraUtc
        };
        Db.Usuarios.Add(usuario);
        await Db.SaveChangesAsync();
        return usuario;
    }

    public async Task<Endereco> CriarEnderecoAsync(int usuarioId, string cep = CepCliente)
    {
        var endereco = new Endereco
        {
            UsuarioId = usuarioId,
            Apelido = "Casa",
            Destinatario = "Maria da Silva",
            Cep = cep,
            Logradouro = "Rua das Flores",
            Numero = "100",
            Bairro = "Centro",
            Cidade = "Campinas",
            Uf = "SP",
            Padrao = true,
            CriadoEm = Relogio.AgoraUtc
        };
        Db.Enderecos.Add(endereco);
        await Db.SaveChangesAsync();
        return endereco;
    }

    public async Task AdicionarFaixaAsync(string inicio, string fim, decimal valor, int prazo)
    {
        Db.FaixasFrete.Add(new FaixaFrete { Descricao = $"Faixa {inicio}", CepInicio = inicio, CepFim = fim, Valor = valor, PrazoDias = prazo, CriadoEm = Relogio.AgoraUtc });
        await Db.SaveChangesAsync();
    }

    public async Task DefinirConfiguracaoAsync(string chave, string valor)
    {
        var c = await Db.Configuracoes.FirstAsync(x => x.Chave == chave);
        c.Valor = valor;
        await Db.SaveChangesAsync();
    }

    /// <summary>Cliente com endereço, produto no carrinho e pedido criado pelo fluxo real de checkout.</summary>
    public async Task<(Usuario Cliente, Produto Produto, int PedidoId)> CriarPedidoAsync(int quantidade = 2, int estoque = 10, decimal preco = 100m)
    {
        var cliente = await CriarClienteAsync();
        var endereco = await CriarEnderecoAsync(cliente.Id);
        var produto = await CriarProdutoAsync(estoque, preco);
        var adicionado = await Carrinhos.AdicionarAsync(new IdentificacaoCarrinho(null, cliente.Id), produto.Id, quantidade);
        Assert.True(adicionado.Sucesso, adicionado.Erro);

        var pedido = await Checkout.CriarPedidoAsync(new ComandoCriarPedido(cliente.Id, Guid.NewGuid(), endereco.Id, "ME-1"));
        Assert.True(pedido.Sucesso, pedido.Erro);
        return (cliente, produto, pedido.Valor);
    }

    /// <summary>Lê o produto direto do banco (sem cache do contexto).</summary>
    public async Task<Produto> RecarregarProdutoAsync(int id)
    {
        await using var db = NovoContexto();
        return await db.Produtos.AsNoTracking().FirstAsync(p => p.Id == id);
    }

    public async Task<Domain.Pedidos.Pedido> RecarregarPedidoAsync(int id)
    {
        await using var db = NovoContexto();
        return await db.Pedidos.AsNoTracking().Include(p => p.Itens).Include(p => p.Pagamentos).Include(p => p.Historico)
            .FirstAsync(p => p.Id == id);
    }

    public void Dispose()
    {
        Db.Dispose();
        _conexao.Dispose();
    }
}
