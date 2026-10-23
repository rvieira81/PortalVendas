using PortalVendas.Application.Comum;

namespace PortalVendas.Application.Licenciamento;

/// <summary>Conteúdo assinado da licença. Só quem tem a chave privada (o fornecedor do sistema) consegue emitir.</summary>
public sealed record DadosLicenca
{
    public Guid Id { get; init; }
    public string Produto { get; init; } = string.Empty;
    public string NomeLoja { get; init; } = string.Empty;
    public string RazaoSocial { get; init; } = string.Empty;
    public string Cnpj { get; init; } = string.Empty;
    /// <summary>Domínios em que a loja pode funcionar (ex.: "www.minhaloja.com.br", "minhaloja.com.br").</summary>
    public IReadOnlyList<string> Dominios { get; init; } = Array.Empty<string>();
    public DateTime EmitidaEm { get; init; }
    /// <summary>Null = licença perpétua.</summary>
    public DateTime? ExpiraEm { get; init; }
    /// <summary>Até quando a loja tem direito a novas versões (informativo).</summary>
    public DateTime? AtualizacoesAte { get; init; }
}

public enum SituacaoLicenca
{
    /// <summary>Sem arquivo de licença: funciona só em localhost, com a marca de demonstração.</summary>
    Demonstracao,
    Valida,
    Expirada,
    Invalida
}

public sealed record EstadoLicenca(SituacaoLicenca Situacao, string Mensagem, DadosLicenca? Dados)
{
    public bool Ativa => Situacao == SituacaoLicenca.Valida;
}

/// <summary>Licença da instalação. O nome, a razão social e o CNPJ da loja vêm daqui e não podem ser editados no painel.</summary>
public interface ILicencaLoja
{
    EstadoLicenca Estado { get; }

    /// <summary>O site pode responder neste endereço? (domínio licenciado, ou localhost acessado da própria máquina)</summary>
    bool HostAutorizado(string host, bool requisicaoLocal);

    /// <summary>Valida e instala um novo arquivo de licença enviado pelo painel.</summary>
    Task<Resultado> InstalarAsync(byte[] conteudo, CancellationToken ct = default);
}
