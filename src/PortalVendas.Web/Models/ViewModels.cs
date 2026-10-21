using System.ComponentModel.DataAnnotations;
using PortalVendas.Application.Carrinhos;
using PortalVendas.Application.Catalogo;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Frete;
using PortalVendas.Application.Pedidos;
using PortalVendas.Domain.Pagamentos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Domain.Usuarios;

namespace PortalVendas.Web.Models;

public class VitrineViewModel
{
    public required Pagina<ProdutoResumo> Produtos { get; init; }
    public required IReadOnlyList<CategoriaResumo> Categorias { get; init; }
    public string? Termo { get; init; }
    public string? Categoria { get; init; }
    public string? PrecoMin { get; init; }
    public string? PrecoMax { get; init; }
    public OrdemVitrine Ordem { get; init; }
    public string? NomeCategoria => Categorias.FirstOrDefault(c => c.Slug == Categoria)?.Nome;
}

public class EntrarViewModel
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Senha { get; set; } = string.Empty;

    [Display(Name = "Manter conectado neste dispositivo")]
    public bool Lembrar { get; set; }

    public string? RetornarPara { get; set; }
}

public class CadastroViewModel
{
    [Required(ErrorMessage = "Informe nome e sobrenome.")]
    [StringLength(150)]
    [Display(Name = "Nome completo")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(254)]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o CPF.")]
    [Display(Name = "CPF")]
    public string Cpf { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o telefone.")]
    [Display(Name = "Celular com DDD")]
    public string Telefone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Crie uma senha.")]
    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "A senha deve ter de 8 a 128 caracteres.")]
    [Display(Name = "Senha")]
    public string Senha { get; set; } = string.Empty;

    [Required(ErrorMessage = "Repita a senha.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Senha), ErrorMessage = "As senhas não conferem.")]
    [Display(Name = "Repita a senha")]
    public string ConfirmarSenha { get; set; } = string.Empty;

    [Display(Name = "Li e aceito os termos de uso e a política de privacidade")]
    public bool AceitouTermos { get; set; }

    public string? RetornarPara { get; set; }
}

public class CodigoViewModel
{
    [Required(ErrorMessage = "Informe o código.")]
    [StringLength(7, MinimumLength = 6, ErrorMessage = "O código tem 6 dígitos.")]
    [Display(Name = "Código do aplicativo autenticador")]
    public string Codigo { get; set; } = string.Empty;
}

public class EsqueciSenhaViewModel
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;
}

public class RedefinirSenhaViewModel
{
    [Required] public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Crie a nova senha.")]
    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "A senha deve ter de 8 a 128 caracteres.")]
    [Display(Name = "Nova senha")]
    public string Senha { get; set; } = string.Empty;

    [Required(ErrorMessage = "Repita a senha.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Senha), ErrorMessage = "As senhas não conferem.")]
    [Display(Name = "Repita a nova senha")]
    public string ConfirmarSenha { get; set; } = string.Empty;
}

public class MinhaContaViewModel
{
    public string Email { get; set; } = string.Empty;
    public bool EmailConfirmado { get; set; }
    public string CpfMascarado { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe nome e sobrenome.")]
    [StringLength(150)]
    [Display(Name = "Nome completo")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o telefone.")]
    [Display(Name = "Celular com DDD")]
    public string Telefone { get; set; } = string.Empty;
}

public class AlterarSenhaViewModel
{
    [Required(ErrorMessage = "Informe a senha atual.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha atual")]
    public string SenhaAtual { get; set; } = string.Empty;

    [Required(ErrorMessage = "Crie a nova senha.")]
    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "A senha deve ter de 8 a 128 caracteres.")]
    [Display(Name = "Nova senha")]
    public string NovaSenha { get; set; } = string.Empty;

    [Required(ErrorMessage = "Repita a nova senha.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NovaSenha), ErrorMessage = "As senhas não conferem.")]
    [Display(Name = "Repita a nova senha")]
    public string ConfirmarSenha { get; set; } = string.Empty;
}

public class EnderecoViewModel
{
    public int Id { get; set; }

    [StringLength(50)]
    [Display(Name = "Apelido (ex.: Casa, Trabalho)")]
    public string? Apelido { get; set; }

    [Required(ErrorMessage = "Informe quem vai receber.")]
    [StringLength(150)]
    [Display(Name = "Nome de quem vai receber")]
    public string Destinatario { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o CEP.")]
    [Display(Name = "CEP")]
    public string Cep { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a rua.")]
    [StringLength(200)]
    [Display(Name = "Rua / Avenida")]
    public string Logradouro { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o número.")]
    [StringLength(20)]
    [Display(Name = "Número")]
    public string Numero { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Complemento")]
    public string? Complemento { get; set; }

    [Required(ErrorMessage = "Informe o bairro.")]
    [StringLength(100)]
    [Display(Name = "Bairro")]
    public string Bairro { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a cidade.")]
    [StringLength(100)]
    [Display(Name = "Cidade")]
    public string Cidade { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a UF.")]
    [StringLength(2, MinimumLength = 2)]
    [Display(Name = "UF")]
    public string Uf { get; set; } = string.Empty;

    [Display(Name = "Usar como endereço padrão")]
    public bool Padrao { get; set; }

    public string? RetornarPara { get; set; }

    public static EnderecoViewModel De(Endereco e) => new()
    {
        Id = e.Id, Apelido = e.Apelido, Destinatario = e.Destinatario, Cep = Domain.Comum.Cep.Formatar(e.Cep),
        Logradouro = e.Logradouro, Numero = e.Numero, Complemento = e.Complemento, Bairro = e.Bairro,
        Cidade = e.Cidade, Uf = e.Uf, Padrao = e.Padrao
    };

    public Endereco ParaEntidade() => new()
    {
        Id = Id, Apelido = Apelido ?? "Endereço", Destinatario = Destinatario, Cep = Cep, Logradouro = Logradouro,
        Numero = Numero, Complemento = Complemento, Bairro = Bairro, Cidade = Cidade, Uf = Uf, Padrao = Padrao
    };
}

public class CheckoutViewModel
{
    public required CarrinhoView Carrinho { get; init; }
    public required IReadOnlyList<Endereco> Enderecos { get; init; }
    public int? EnderecoSelecionadoId { get; init; }
    public IReadOnlyList<OpcaoFrete> OpcoesFrete { get; init; } = Array.Empty<OpcaoFrete>();
    public string? ErroFrete { get; init; }
    public string? CodigoFreteSelecionado { get; init; }
    public Guid ChaveIdempotencia { get; init; } = Guid.NewGuid();
    public bool EmailConfirmado { get; init; }

    public OpcaoFrete? FreteSelecionado => OpcoesFrete.FirstOrDefault(o => o.Codigo == CodigoFreteSelecionado) ?? OpcoesFrete.FirstOrDefault();
    public decimal Total => Carrinho.Subtotal + (FreteSelecionado?.Valor ?? 0);
}

public class ConfirmarPedidoInput
{
    public int EnderecoId { get; set; }
    [Required] public string CodigoFrete { get; set; } = string.Empty;
    public Guid ChaveIdempotencia { get; set; }
}

public class PagamentoViewModel
{
    public required Pedido Pedido { get; init; }
    public Pagamento? PixVigente { get; init; }
    public Pagamento? BoletoVigente { get; init; }
    public required string PublicKey { get; init; }
    public required int ParcelasMaximas { get; init; }
    public required string EmailPagador { get; init; }
    public string? AbaInicial { get; init; }
}

public class MeusPedidosViewModel
{
    public required Pagina<PedidoResumo> Pedidos { get; init; }
}

/// <summary>Dados enviados pelo Card Payment Brick (JSON). O valor é ignorado: o servidor usa o total do pedido.</summary>
public class DadosCartaoInput
{
    [System.Text.Json.Serialization.JsonPropertyName("token")]
    public string? Token { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("payment_method_id")]
    public string? PaymentMethodId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("issuer_id")]
    public System.Text.Json.JsonElement? IssuerId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("installments")]
    public int Installments { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("payer")]
    public PagadorBrickInput? Payer { get; set; }
}

public class PagadorBrickInput
{
    [System.Text.Json.Serialization.JsonPropertyName("email")]
    public string? Email { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("identification")]
    public IdentificacaoBrickInput? Identification { get; set; }
}

public class IdentificacaoBrickInput
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string? Type { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("number")]
    public string? Number { get; set; }
}

public class ErroViewModel
{
    public int Codigo { get; init; }
    public string? RequestId { get; init; }
    public string Titulo => Codigo switch
    {
        404 => "Página não encontrada",
        403 => "Acesso negado",
        429 => "Muitas tentativas",
        _ => "Algo deu errado"
    };
    public string Mensagem => Codigo switch
    {
        404 => "O endereço que você acessou não existe ou o item não está mais disponível.",
        403 => "Você não tem permissão para acessar esta página.",
        429 => "Você fez muitas tentativas em pouco tempo. Aguarde um minuto e tente de novo.",
        _ => "Tivemos um problema ao processar sua solicitação. Tente novamente em instantes."
    };
}
