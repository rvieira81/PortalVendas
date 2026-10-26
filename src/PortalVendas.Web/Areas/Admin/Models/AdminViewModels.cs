using System.ComponentModel.DataAnnotations;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Pedidos;
using PortalVendas.Domain.Catalogo;
using PortalVendas.Domain.Frete;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Web.Infra;

namespace PortalVendas.Web.Areas.Admin.Models;

public class ProdutosIndexViewModel
{
    public required Pagina<ProdutoAdminResumo> Produtos { get; init; }
    public required IReadOnlyList<Categoria> Categorias { get; init; }
    public string? Termo { get; init; }
    public int? CategoriaId { get; init; }
}

/// <summary>Valores monetários e medidas como texto: aceitam "1.234,56" (pt-BR) e são convertidos no controller.</summary>
public class ProdutoEdicaoViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Escolha a categoria.")]
    [Display(Name = "Categoria")]
    public int CategoriaId { get; set; }

    [Required(ErrorMessage = "Informe o SKU.")]
    [StringLength(50)]
    [Display(Name = "SKU (código interno)")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(200)]
    [Display(Name = "Nome")]
    public string Nome { get; set; } = string.Empty;

    [StringLength(4000)]
    [Display(Name = "Descrição")]
    public string? Descricao { get; set; }

    [Required(ErrorMessage = "Informe o preço.")]
    [Display(Name = "Preço de venda (R$)")]
    public string Preco { get; set; } = string.Empty;

    [Display(Name = "Preço \"de\" (R$, opcional)")]
    public string? PrecoDe { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Estoque inválido.")]
    [Display(Name = "Estoque físico")]
    public int Estoque { get; set; }

    [Required(ErrorMessage = "Informe o peso.")]
    [Display(Name = "Peso (kg)")]
    public string PesoKg { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a altura.")]
    [Display(Name = "Altura (cm)")]
    public string AlturaCm { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a largura.")]
    [Display(Name = "Largura (cm)")]
    public string LarguraCm { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o comprimento.")]
    [Display(Name = "Comprimento (cm)")]
    public string ComprimentoCm { get; set; } = string.Empty;

    [Display(Name = "Ativo (visível na loja)")]
    public bool Ativo { get; set; } = true;

    public int EstoqueReservado { get; set; }
    public string? Slug { get; set; }
    public IReadOnlyList<ProdutoImagem> Imagens { get; set; } = Array.Empty<ProdutoImagem>();
    public IReadOnlyList<Categoria> Categorias { get; set; } = Array.Empty<Categoria>();

    public static ProdutoEdicaoViewModel De(Produto p) => new()
    {
        Id = p.Id,
        CategoriaId = p.CategoriaId,
        Sku = p.Sku,
        Nome = p.Nome,
        Descricao = p.Descricao,
        Preco = Formatacao.Numero(p.Preco),
        PrecoDe = p.PrecoDe is null ? null : Formatacao.Numero(p.PrecoDe.Value),
        Estoque = p.Estoque,
        PesoKg = Formatacao.Numero(p.PesoKg, 3),
        AlturaCm = Formatacao.Numero(p.AlturaCm, 1),
        LarguraCm = Formatacao.Numero(p.LarguraCm, 1),
        ComprimentoCm = Formatacao.Numero(p.ComprimentoCm, 1),
        Ativo = p.Ativo,
        EstoqueReservado = p.EstoqueReservado,
        Slug = p.Slug,
        Imagens = p.Imagens.OrderBy(i => i.Ordem).ToList()
    };
}

public class PedidosAdminIndexViewModel
{
    public PortalVendas.Domain.Pedidos.OrigemPedido? Origem { get; init; }
    public required Pagina<PedidoAdminResumo> Pedidos { get; init; }
    public StatusPedido? Status { get; init; }
    public string? De { get; init; }
    public string? Ate { get; init; }
    public string? Termo { get; init; }
}

public class FreteAdminViewModel
{
    public required ConfiguracaoLoja Configuracao { get; init; }
    public required IReadOnlyList<FaixaFrete> Faixas { get; init; }
}

public class ConfiguracaoInput
{
    public string CepOrigem { get; set; } = string.Empty;
    public int DiasManuseio { get; set; }
    public string? FreteGratisAPartirDe { get; set; }
    public int ParcelasMaximas { get; set; } = 12;
    public string? ParcelaMinima { get; set; }
}

public class FaixaInput
{
    public int Id { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string CepInicio { get; set; } = string.Empty;
    public string CepFim { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public int PrazoDias { get; set; }
    public bool Ativa { get; set; } = true;
}

public class ConfigurarMfaViewModel
{
    public required string Segredo { get; init; }
    public required string SegredoFormatado { get; init; }
    public required string UriOtpAuth { get; init; }

    [Required(ErrorMessage = "Informe o código.")]
    public string? Codigo { get; set; }
}

public class IdentidadeAdminViewModel
{
    public required PortalVendas.Application.Admin.IdentidadeLoja Identidade { get; init; }
    public required PortalVendas.Application.Licenciamento.EstadoLicenca Licenca { get; init; }
}

public class IdentidadeInput
{
    public string CorPrincipal { get; set; } = string.Empty;
    public string CorDestaque { get; set; } = string.Empty;
    public string? WhatsApp { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Endereco { get; set; }
    public string? Horario { get; set; }
    public string? Instagram { get; set; }
    public string? Facebook { get; set; }
    public string? Youtube { get; set; }
    public string? AvisoTopo { get; set; }
    public string? BannerSelo { get; set; }
    public string? BannerTitulo { get; set; }
    public string? BannerTexto { get; set; }
    public string? BannerBotao { get; set; }
    public string? BannerLink { get; set; }
}

public class WhatsAppIndexViewModel
{
    public required PortalVendas.Application.Comum.Pagina<PortalVendas.Application.WhatsApp.ConversaResumo> Conversas { get; init; }
    public bool SomenteAtendente { get; init; }
    public string? Termo { get; init; }
    public bool Configurado { get; init; }
    public bool RoboAtivo { get; init; }
}

public class WhatsAppConfiguracaoViewModel
{
    public PortalVendas.Application.WhatsApp.ProvedorWhatsApp Provedor { get; init; }
    public bool RoboAtivo { get; init; }
    public string? BoasVindas { get; init; }
    public string? MetaPhoneNumberId { get; init; }
    public bool MetaTemToken { get; init; }
    public bool MetaTemAppSecret { get; init; }
    public string? MetaModeloAviso { get; init; }
    public string MetaIdiomaModelo { get; init; } = "pt_BR";
    public string? EvolutionUrl { get; init; }
    public string? EvolutionInstancia { get; init; }
    public bool EvolutionTemApiKey { get; init; }
    public bool IaAtiva { get; init; }
    public bool IaTemChave { get; init; }
    public string IaModelo { get; init; } = string.Empty;
    public string UrlWebhookMeta { get; init; } = string.Empty;
    public string MetaVerifyToken { get; init; } = string.Empty;
    public string UrlWebhookEvolution { get; init; } = string.Empty;
}

public class ConfiguracaoWhatsAppInput
{
    public PortalVendas.Application.WhatsApp.ProvedorWhatsApp Provedor { get; set; }
    public bool RoboAtivo { get; set; }
    public string? BoasVindas { get; set; }
    public string? MetaPhoneNumberId { get; set; }
    public string? MetaToken { get; set; }
    public string? MetaAppSecret { get; set; }
    public string? MetaModeloAviso { get; set; }
    public string? MetaIdiomaModelo { get; set; }
    public string? EvolutionUrl { get; set; }
    public string? EvolutionInstancia { get; set; }
    public string? EvolutionApiKey { get; set; }
    public bool IaAtiva { get; set; }
    public string? IaChaveApi { get; set; }
    public string? IaModelo { get; set; }
}

public class WhatsAppSimuladorViewModel
{
    public required string Telefone { get; init; }
    public bool Ativo { get; init; }
    public bool RoboAtivo { get; init; }
    public PortalVendas.Application.WhatsApp.ConversaDetalhe? Conversa { get; init; }
}
