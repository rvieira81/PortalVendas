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
    /// <summary>Módulos pagos liberados para esta loja (ex.: "whatsapp"). Licenças antigas vêm sem a lista.</summary>
    public IReadOnlyList<string>? Modulos { get; init; }
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

    /// <summary>O módulo está na licença e a licença está válida (sem licença não há módulo pago, nem em demonstração).</summary>
    public bool TemModulo(string modulo) =>
        Ativa && Dados?.Modulos is { } modulos && modulos.Any(m => string.Equals(m, modulo, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Módulos vendidos à parte.</summary>
public static class ModulosLicenca
{
    public const string WhatsApp = "whatsapp";

    public static readonly IReadOnlyList<string> Todos = new[] { WhatsApp };
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
