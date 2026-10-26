using Microsoft.Extensions.Options;
using PortalVendas.Application.Abstracoes;

namespace PortalVendas.Infrastructure.Servicos;

public class LojaOptions
{
    public const string Secao = "Loja";
    // Nome, razão social e CNPJ não ficam mais aqui: vêm da licença (painel → Identidade da loja).
    /// <summary>URL pública do site, com https e sem barra no fim. Usada nos links dos e-mails.</summary>
    public string UrlBase { get; set; } = "https://localhost:5001";
    public string EmailAtendimento { get; set; } = "atendimento@sualoja.com.br";
    /// <summary>Usados só enquanto o lojista não preenche os contatos no painel.</summary>
    public string? TelefoneAtendimento { get; set; }
}

/// <summary>Links absolutos a partir da URL configurada, nunca do cabeçalho Host (evita host header injection).</summary>
public class LinksAplicacao : ILinksAplicacao
{
    private readonly string _base;
    private readonly string _caminhoImagens;

    public LinksAplicacao(IOptions<LojaOptions> opcoes, IOptions<ImagensOptions> imagens)
    {
        _base = opcoes.Value.UrlBase.TrimEnd('/');
        _caminhoImagens = "/" + imagens.Value.CaminhoPublico.Trim('/');
    }

    public string ConfirmarEmail(string token) => $"{_base}/conta/confirmar-email?token={Uri.EscapeDataString(token)}";
    public string RedefinirSenha(string token) => $"{_base}/conta/redefinir-senha?token={Uri.EscapeDataString(token)}";
    public string Pedido(int pedidoId) => $"{_base}/pedidos/{pedidoId}";
    public string Inicio() => _base + "/";
    public string AcessoWhatsApp(string token, int pedidoId) =>
        $"{_base}/conta/acesso-whatsapp?token={Uri.EscapeDataString(token)}&pedido={pedidoId}";
    public string Imagem(string arquivo) => $"{_base}{_caminhoImagens}/{Uri.EscapeDataString(arquivo)}";
}
