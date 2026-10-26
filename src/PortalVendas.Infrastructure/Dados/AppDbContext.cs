using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Domain.Carrinhos;
using PortalVendas.Domain.Catalogo;
using PortalVendas.Domain.Frete;
using PortalVendas.Domain.Pagamentos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Domain.Usuarios;
using PortalVendas.Domain.WhatsApp;

namespace PortalVendas.Infrastructure.Dados;

/// <summary>
/// Mapeia as tabelas criadas pelos scripts de db/migrations (ADR-003). O EF nunca cria nem altera o esquema
/// em produção; o mapeamento evita recursos exclusivos do SQL Server para os testes rodarem em SQLite.
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ProdutoImagem> ProdutoImagens => Set<ProdutoImagem>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<TokenUsuario> TokensUsuario => Set<TokenUsuario>();
    public DbSet<Endereco> Enderecos => Set<Endereco>();
    public DbSet<Carrinho> Carrinhos => Set<Carrinho>();
    public DbSet<CarrinhoItem> CarrinhoItens => Set<CarrinhoItem>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoItem> PedidoItens => Set<PedidoItem>();
    public DbSet<PedidoHistorico> PedidoHistoricos => Set<PedidoHistorico>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<NotificacaoGateway> NotificacoesGateway => Set<NotificacaoGateway>();
    public DbSet<FaixaFrete> FaixasFrete => Set<FaixaFrete>();
    public DbSet<Configuracao> Configuracoes => Set<Configuracao>();
    public DbSet<ConversaWhatsApp> ConversasWhatsApp => Set<ConversaWhatsApp>();
    public DbSet<MensagemWhatsApp> MensagensWhatsApp => Set<MensagemWhatsApp>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Dinheiro: decimal(12,2), salvo exceções configuradas no mapeamento (peso e medidas)
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
    }
}
