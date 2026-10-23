using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PortalVendas.Domain.Carrinhos;
using PortalVendas.Domain.Catalogo;
using PortalVendas.Domain.Frete;
using PortalVendas.Domain.Pagamentos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Domain.Usuarios;
using PortalVendas.Domain.WhatsApp;

namespace PortalVendas.Application.Abstracoes;

public interface IAppDbContext
{
    DbSet<Categoria> Categorias { get; }
    DbSet<Produto> Produtos { get; }
    DbSet<ProdutoImagem> ProdutoImagens { get; }
    DbSet<Usuario> Usuarios { get; }
    DbSet<TokenUsuario> TokensUsuario { get; }
    DbSet<Endereco> Enderecos { get; }
    DbSet<Carrinho> Carrinhos { get; }
    DbSet<CarrinhoItem> CarrinhoItens { get; }
    DbSet<Pedido> Pedidos { get; }
    DbSet<PedidoItem> PedidoItens { get; }
    DbSet<PedidoHistorico> PedidoHistoricos { get; }
    DbSet<Pagamento> Pagamentos { get; }
    DbSet<NotificacaoGateway> NotificacoesGateway { get; }
    DbSet<FaixaFrete> FaixasFrete { get; }
    DbSet<Configuracao> Configuracoes { get; }
    DbSet<ConversaWhatsApp> ConversasWhatsApp { get; }
    DbSet<MensagemWhatsApp> MensagensWhatsApp { get; }

    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
