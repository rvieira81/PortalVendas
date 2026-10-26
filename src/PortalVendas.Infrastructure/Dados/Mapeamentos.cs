using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PortalVendas.Domain.Carrinhos;
using PortalVendas.Domain.Catalogo;
using PortalVendas.Domain.Frete;
using PortalVendas.Domain.Pagamentos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Domain.Usuarios;
using PortalVendas.Domain.WhatsApp;

namespace PortalVendas.Infrastructure.Dados;

// Colunas VARCHAR/CHAR são mapeadas com IsUnicode(false): o EF envia parâmetros varchar e o SQL Server
// usa os índices sem conversão implícita.

internal sealed class CategoriaMap : IEntityTypeConfiguration<Categoria>
{
    public void Configure(EntityTypeBuilder<Categoria> b)
    {
        b.ToTable("Categoria");
        b.HasKey(x => x.Id);
        b.Property(x => x.Nome).HasMaxLength(100).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(120).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => x.Nome).IsUnique();
    }
}

internal sealed class ProdutoMap : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> b)
    {
        b.ToTable("Produto");
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(x => x.Nome).HasMaxLength(200).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(220).IsUnicode(false).IsRequired();
        b.Property(x => x.Descricao).HasMaxLength(4000).IsRequired();
        b.Property(x => x.PrecoDe).HasPrecision(12, 2);
        b.Property(x => x.PesoKg).HasPrecision(8, 3);
        b.Property(x => x.AlturaCm).HasPrecision(6, 1);
        b.Property(x => x.LarguraCm).HasPrecision(6, 1);
        b.Property(x => x.ComprimentoCm).HasPrecision(6, 1);
        b.Property(x => x.CriadoPor).HasMaxLength(100).IsRequired();
        b.Property(x => x.AlteradoPor).HasMaxLength(100);
        b.Ignore(x => x.Disponivel);
        b.Ignore(x => x.QuantidadeMaximaCompra);
        b.HasIndex(x => x.Sku).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasOne(x => x.Categoria).WithMany(c => c.Produtos).HasForeignKey(x => x.CategoriaId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Imagens).WithOne().HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProdutoImagemMap : IEntityTypeConfiguration<ProdutoImagem>
{
    public void Configure(EntityTypeBuilder<ProdutoImagem> b)
    {
        b.ToTable("ProdutoImagem");
        b.HasKey(x => x.Id);
        b.Property(x => x.Arquivo).HasMaxLength(100).IsUnicode(false).IsRequired();
    }
}

internal sealed class UsuarioMap : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.ToTable("Usuario");
        b.HasKey(x => x.Id);
        b.Property(x => x.Nome).HasMaxLength(150).IsRequired();
        b.Property(x => x.Email).HasMaxLength(254).IsUnicode(false).IsRequired();
        b.Property(x => x.Cpf).HasMaxLength(11).IsUnicode(false).IsFixedLength();
        b.Property(x => x.Telefone).HasMaxLength(11).IsUnicode(false);
        b.Property(x => x.SenhaHash).HasMaxLength(256).IsUnicode(false).IsRequired();
        b.Property(x => x.TotpSegredoProtegido).HasMaxLength(500).IsUnicode(false);
        b.Ignore(x => x.EhAdministrador);
        b.HasIndex(x => x.Email).IsUnique();
        b.HasMany(x => x.Enderecos).WithOne().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TokenUsuarioMap : IEntityTypeConfiguration<TokenUsuario>
{
    public void Configure(EntityTypeBuilder<TokenUsuario> b)
    {
        b.ToTable("TokenUsuario");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EnderecoMap : IEntityTypeConfiguration<Endereco>
{
    public void Configure(EntityTypeBuilder<Endereco> b)
    {
        b.ToTable("Endereco");
        b.HasKey(x => x.Id);
        b.Property(x => x.Apelido).HasMaxLength(50).IsRequired();
        b.Property(x => x.Destinatario).HasMaxLength(150).IsRequired();
        b.Property(x => x.Cep).HasMaxLength(8).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(x => x.Logradouro).HasMaxLength(200).IsRequired();
        b.Property(x => x.Numero).HasMaxLength(20).IsRequired();
        b.Property(x => x.Complemento).HasMaxLength(100);
        b.Property(x => x.Bairro).HasMaxLength(100).IsRequired();
        b.Property(x => x.Cidade).HasMaxLength(100).IsRequired();
        b.Property(x => x.Uf).HasMaxLength(2).IsUnicode(false).IsFixedLength().IsRequired();
        b.Ignore(x => x.Resumo);
    }
}

internal sealed class CarrinhoMap : IEntityTypeConfiguration<Carrinho>
{
    public void Configure(EntityTypeBuilder<Carrinho> b)
    {
        b.ToTable("Carrinho");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Token).IsUnique();
        b.Ignore(x => x.EstaVazio);
        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Itens).WithOne().HasForeignKey(i => i.CarrinhoId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CarrinhoItemMap : IEntityTypeConfiguration<CarrinhoItem>
{
    public void Configure(EntityTypeBuilder<CarrinhoItem> b)
    {
        b.ToTable("CarrinhoItem");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CarrinhoId, x.ProdutoId }).IsUnique();
        b.HasOne(x => x.Produto).WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PedidoMap : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> b)
    {
        b.ToTable("Pedido");
        b.HasKey(x => x.Id);
        b.Property(x => x.FreteDescricao).HasMaxLength(150).IsRequired();
        b.Property(x => x.CodigoRastreio).HasMaxLength(50).IsUnicode(false);
        b.Property(x => x.MotivoCancelamento).HasMaxLength(300);
        b.Property(x => x.Versao).IsConcurrencyToken();
        b.HasIndex(x => x.ChaveIdempotencia).IsUnique();
        b.Ignore(x => x.Numero);
        b.Ignore(x => x.EstoqueBaixado);
        b.Ignore(x => x.PodeReceberPagamento);
        b.Ignore(x => x.PodeSerCanceladoPeloCliente);

        b.OwnsOne(x => x.Entrega, e =>
        {
            e.Property(p => p.Destinatario).HasColumnName("EntregaDestinatario").HasMaxLength(150).IsRequired();
            e.Property(p => p.Cep).HasColumnName("EntregaCep").HasMaxLength(8).IsUnicode(false).IsFixedLength().IsRequired();
            e.Property(p => p.Logradouro).HasColumnName("EntregaLogradouro").HasMaxLength(200).IsRequired();
            e.Property(p => p.Numero).HasColumnName("EntregaNumero").HasMaxLength(20).IsRequired();
            e.Property(p => p.Complemento).HasColumnName("EntregaComplemento").HasMaxLength(100);
            e.Property(p => p.Bairro).HasColumnName("EntregaBairro").HasMaxLength(100).IsRequired();
            e.Property(p => p.Cidade).HasColumnName("EntregaCidade").HasMaxLength(100).IsRequired();
            e.Property(p => p.Uf).HasColumnName("EntregaUf").HasMaxLength(2).IsUnicode(false).IsFixedLength().IsRequired();
            e.Ignore(p => p.Resumo);
        });
        b.Navigation(x => x.Entrega).IsRequired();

        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Itens).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Historico).WithOne().HasForeignKey(h => h.PedidoId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Pagamentos).WithOne().HasForeignKey(p => p.PedidoId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Historico).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Pagamentos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PedidoItemMap : IEntityTypeConfiguration<PedidoItem>
{
    public void Configure(EntityTypeBuilder<PedidoItem> b)
    {
        b.ToTable("PedidoItem");
        b.HasKey(x => x.Id);
        b.Property(x => x.NomeProduto).HasMaxLength(200).IsRequired();
        b.Property(x => x.Sku).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.HasOne<Produto>().WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PedidoHistoricoMap : IEntityTypeConfiguration<PedidoHistorico>
{
    public void Configure(EntityTypeBuilder<PedidoHistorico> b)
    {
        b.ToTable("PedidoHistorico");
        b.HasKey(x => x.Id);
        b.Property(x => x.Observacao).HasMaxLength(300);
        b.Property(x => x.Autor).HasMaxLength(100).IsRequired();
    }
}

internal sealed class PagamentoMap : IEntityTypeConfiguration<Pagamento>
{
    public void Configure(EntityTypeBuilder<Pagamento> b)
    {
        b.ToTable("Pagamento");
        b.HasKey(x => x.Id);
        b.Property(x => x.StatusGateway).HasMaxLength(30).IsUnicode(false);
        b.Property(x => x.StatusDetalhe).HasMaxLength(100).IsUnicode(false);
        b.Property(x => x.Bandeira).HasMaxLength(30).IsUnicode(false);
        b.Property(x => x.UltimosDigitos).HasMaxLength(4).IsUnicode(false).IsFixedLength();
        b.Property(x => x.PixQrCode).HasMaxLength(1000).IsUnicode(false);
        b.Property(x => x.PixQrCodeBase64).IsUnicode(false);
        b.Property(x => x.BoletoUrl).HasMaxLength(500).IsUnicode(false);
        b.Property(x => x.BoletoLinhaDigitavel).HasMaxLength(60).IsUnicode(false);
        b.HasIndex(x => x.ChaveIdempotencia).IsUnique();
        b.HasIndex(x => x.GatewayId);
    }
}

internal sealed class NotificacaoGatewayMap : IEntityTypeConfiguration<NotificacaoGateway>
{
    public void Configure(EntityTypeBuilder<NotificacaoGateway> b)
    {
        b.ToTable("NotificacaoGateway");
        b.HasKey(x => x.Id);
        b.Property(x => x.RequestId).HasMaxLength(100).IsUnicode(false);
        b.Property(x => x.Topico).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(x => x.RecursoId).HasMaxLength(50).IsUnicode(false).IsRequired();
        b.Property(x => x.Erro).HasMaxLength(1000);
    }
}

internal sealed class FaixaFreteMap : IEntityTypeConfiguration<FaixaFrete>
{
    public void Configure(EntityTypeBuilder<FaixaFrete> b)
    {
        b.ToTable("FaixaFrete");
        b.HasKey(x => x.Id);
        b.Property(x => x.Descricao).HasMaxLength(100).IsRequired();
        b.Property(x => x.CepInicio).HasMaxLength(8).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(x => x.CepFim).HasMaxLength(8).IsUnicode(false).IsFixedLength().IsRequired();
    }
}

internal sealed class ConfiguracaoMap : IEntityTypeConfiguration<Configuracao>
{
    public void Configure(EntityTypeBuilder<Configuracao> b)
    {
        b.ToTable("Configuracao");
        b.HasKey(x => x.Chave);
        b.Property(x => x.Chave).HasMaxLength(100).IsUnicode(false);
        b.Property(x => x.Valor).HasMaxLength(2000).IsRequired();
        b.Property(x => x.AlteradoPor).HasMaxLength(100);
    }
}

internal sealed class ConversaWhatsAppMap : IEntityTypeConfiguration<ConversaWhatsApp>
{
    public void Configure(EntityTypeBuilder<ConversaWhatsApp> b)
    {
        b.ToTable("ConversaWhatsApp");
        b.HasKey(x => x.Id);
        b.Property(x => x.Telefone).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(x => x.NomeContato).HasMaxLength(150);
        b.HasIndex(x => x.Telefone).IsUnique();
        b.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Mensagens).WithOne().HasForeignKey(m => m.ConversaId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MensagemWhatsAppMap : IEntityTypeConfiguration<MensagemWhatsApp>
{
    public void Configure(EntityTypeBuilder<MensagemWhatsApp> b)
    {
        b.ToTable("MensagemWhatsApp");
        b.HasKey(x => x.Id);
        b.Property(x => x.Texto).HasMaxLength(4000).IsRequired();
        b.Property(x => x.IdExterno).HasMaxLength(200).IsUnicode(false);
        b.Property(x => x.Autor).HasMaxLength(100);
        b.Property(x => x.Erro).HasMaxLength(500);
        b.HasIndex(x => x.IdExterno).IsUnique().HasFilter("[IdExterno] IS NOT NULL");
    }
}
