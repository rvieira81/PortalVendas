using Microsoft.Extensions.Options;
using PortalVendas.Application.Abstracoes;

namespace PortalVendas.Infrastructure.Servicos;

public class LojaOptions
{
    public const string Secao = "Loja";
    public string Nome { get; set; } = "Portal de Vendas";
    /// <summary>URL pública do site, com https e sem barra no fim. Usada nos links dos e-mails.</summary>
    public string UrlBase { get; set; } = "https://localhost:5001";
    public string EmailAtendimento { get; set; } = "atendimento@sualoja.com.br";
    public string? TelefoneAtendimento { get; set; }
    public string? Cnpj { get; set; }
}

/// <summary>Links absolutos a partir da URL configurada, nunca do cabeçalho Host (evita host header injection).</summary>
public class LinksAplicacao : ILinksAplicacao
{
    private readonly string _base;

    public LinksAplicacao(IOptions<LojaOptions> opcoes) => _base = opcoes.Value.UrlBase.TrimEnd('/');

    public string ConfirmarEmail(string token) => $"{_base}/conta/confirmar-email?token={Uri.EscapeDataString(token)}";
    public string RedefinirSenha(string token) => $"{_base}/conta/redefinir-senha?token={Uri.EscapeDataString(token)}";
    public string Pedido(int pedidoId) => $"{_base}/pedidos/{pedidoId}";
    public string Inicio() => _base + "/";
}
