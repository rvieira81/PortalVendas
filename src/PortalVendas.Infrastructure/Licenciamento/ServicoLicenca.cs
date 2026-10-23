using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Licenciamento;

namespace PortalVendas.Infrastructure.Licenciamento;

public class LicencaOptions
{
    public const string Secao = "Licenca";
    /// <summary>Caminho do arquivo .lic. Relativo à pasta do site; em produção, de preferência fora dela (ex.: D:\PortalVendas\licenca.lic).</summary>
    public string Arquivo { get; set; } = "licenca.lic";
}

/// <summary>
/// Carrega e confere a licença na inicialização e quando o administrador envia um novo arquivo pelo painel.
/// Sem licença válida a loja só abre em localhost, com a marca de demonstração.
/// </summary>
public sealed class ServicoLicenca : ILicencaLoja
{
    private static readonly HashSet<string> HostsLocais = new(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "::1", "[::1]" };

    private readonly string _caminho;
    private readonly string _chavePublica;
    private readonly IRelogio _relogio;
    private readonly ILogger<ServicoLicenca> _logger;
    private readonly object _trava = new();
    private DadosLicenca? _dados;
    private string _mensagem = string.Empty;
    private bool _invalida;

    public ServicoLicenca(IOptions<LicencaOptions> opcoes, IHostEnvironment ambiente, IRelogio relogio, ILogger<ServicoLicenca> logger)
        : this(ResolverCaminho(opcoes.Value.Arquivo, ambiente.ContentRootPath), ChavePublicaLicenca.Valor, relogio, logger)
    {
    }

    /// <summary>Testes: permite usar um par de chaves gerado na hora.</summary>
    internal ServicoLicenca(string caminho, string chavePublica, IRelogio relogio, ILogger<ServicoLicenca> logger)
    {
        _caminho = caminho;
        _chavePublica = chavePublica;
        _relogio = relogio;
        _logger = logger;
        Carregar();
    }

    private static string ResolverCaminho(string arquivo, string raiz) =>
        Path.IsPathRooted(arquivo) ? arquivo : Path.Combine(raiz, string.IsNullOrWhiteSpace(arquivo) ? "licenca.lic" : arquivo);

    public EstadoLicenca Estado
    {
        get
        {
            lock (_trava)
            {
                if (_dados is null)
                    return new EstadoLicenca(_invalida ? SituacaoLicenca.Invalida : SituacaoLicenca.Demonstracao, _mensagem, null);
                if (_dados.ExpiraEm is { } expira && expira < _relogio.AgoraUtc)
                    return new EstadoLicenca(SituacaoLicenca.Expirada, $"Licença expirada em {expira:dd/MM/yyyy}. Fale com o fornecedor para renovar.", _dados);
                return new EstadoLicenca(SituacaoLicenca.Valida, "Licença válida.", _dados);
            }
        }
    }

    public bool HostAutorizado(string host, bool requisicaoLocal)
    {
        var h = (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (requisicaoLocal && HostsLocais.Contains(h)) return true;

        var estado = Estado;
        if (!estado.Ativa || estado.Dados is null) return false;
        return estado.Dados.Dominios.Any(d => DominioConfere(d, h));
    }

    internal static bool DominioConfere(string licenciado, string host)
    {
        var d = licenciado.Trim().TrimEnd('.').ToLowerInvariant();
        if (d.StartsWith("*.", StringComparison.Ordinal))
            return host.EndsWith(d[1..], StringComparison.Ordinal) && host.Length > d.Length - 1;
        return host == d;
    }

    public async Task<Resultado> InstalarAsync(byte[] conteudo, CancellationToken ct = default)
    {
        var (dados, erro) = ArquivoLicenca.Ler(conteudo, _chavePublica);
        if (dados is null) return Resultado.Falha(erro ?? "Licença inválida.");
        if (dados.ExpiraEm is { } expira && expira < _relogio.AgoraUtc) return Resultado.Falha("Esta licença já está expirada.");

        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var temporario = _caminho + ".novo";
        await File.WriteAllBytesAsync(temporario, conteudo, ct);
        File.Move(temporario, _caminho, overwrite: true);

        lock (_trava)
        {
            _dados = dados;
            _invalida = false;
            _mensagem = "Licença válida.";
        }
        _logger.LogInformation("Nova licença instalada: {Licenca}", ArquivoLicenca.Descrever(dados));
        return Resultado.Ok();
    }

    private void Carregar()
    {
        if (!File.Exists(_caminho))
        {
            _mensagem = "Nenhuma licença instalada: a loja funciona só em localhost, como demonstração.";
            _logger.LogWarning("Licença não encontrada em {Caminho}: modo demonstração (só localhost)", _caminho);
            return;
        }

        var (dados, erro) = ArquivoLicenca.Ler(File.ReadAllBytes(_caminho), _chavePublica);
        if (dados is null)
        {
            _invalida = true;
            _mensagem = erro ?? "Licença inválida.";
            _logger.LogError("Licença recusada ({Caminho}): {Motivo}", _caminho, _mensagem);
            return;
        }
        _dados = dados;
        _mensagem = "Licença válida.";
        _logger.LogInformation("Licença carregada: {Licenca}", ArquivoLicenca.Descrever(dados));
    }
}
