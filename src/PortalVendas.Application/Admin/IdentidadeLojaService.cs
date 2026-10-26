using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Frete;

namespace PortalVendas.Application.Admin;

/// <summary>
/// Identidade visual e de contato da loja.
/// Nome, razão social e CNPJ vêm da LICENÇA (só o fornecedor altera, emitindo outra licença);
/// o restante é editado pelo administrador da loja no painel.
/// </summary>
public sealed record IdentidadeLoja
{
    // ---- definidos pela licença (somente leitura no painel)
    public required string NomeLoja { get; init; }
    public required string RazaoSocial { get; init; }
    public required string Cnpj { get; init; }
    public required SituacaoLicenca Licenca { get; init; }
    public bool Demonstracao => Licenca != SituacaoLicenca.Valida;

    // ---- editáveis pelo administrador
    public string? Logo { get; init; }
    public string CorPrincipal { get; init; } = IdentidadeLojaService.CorPrincipalPadrao;
    public string CorDestaque { get; init; } = IdentidadeLojaService.CorDestaquePadrao;
    public string? WhatsApp { get; init; }
    public string? Telefone { get; init; }
    public string? Email { get; init; }
    public string? Endereco { get; init; }
    public string? Horario { get; init; }
    public string? Instagram { get; init; }
    public string? Facebook { get; init; }
    public string? Youtube { get; init; }
    public string? AvisoTopo { get; init; }
    public string BannerSelo { get; init; } = "Ofertas da semana";
    public string BannerTitulo { get; init; } = "Ofertas especiais para você";
    public string BannerTexto { get; init; } = "Produtos selecionados com frete calculado na hora para o seu CEP.";
    public string BannerBotao { get; init; } = "Ver ofertas";
    public string BannerLink { get; init; } = "/busca?ordem=MenorPreco";

    public string Inicial => string.IsNullOrWhiteSpace(NomeLoja) ? "L" : NomeLoja.Trim()[..1].ToUpperInvariant();
    public string CnpjFormatado => Domain.Comum.Cnpj.Formatar(Cnpj);
    public string? WhatsAppLink => string.IsNullOrEmpty(WhatsApp) ? null : $"https://wa.me/55{Texto.SomenteDigitos(WhatsApp)}";
}

/// <summary>Campos que o administrador da loja pode alterar.</summary>
public sealed record IdentidadeEditavel(
    string CorPrincipal, string CorDestaque,
    string? WhatsApp, string? Telefone, string? Email, string? Endereco, string? Horario,
    string? Instagram, string? Facebook, string? Youtube, string? AvisoTopo,
    string? BannerSelo, string? BannerTitulo, string? BannerTexto, string? BannerBotao, string? BannerLink);

public sealed class IdentidadeLojaService
{
    public const string CorPrincipalPadrao = "#12343B";
    public const string CorDestaquePadrao = "#F5B82E";
    public const string NomeDemonstracao = "Loja Demonstração";
    public const string CnpjDemonstracao = "00000000000000";

    private const string Prefixo = "Loja.";
    private const string ChaveLogo = "Loja.Logo";

    private readonly IAppDbContext _db;
    private readonly ILicencaLoja _licenca;
    private readonly IArmazenamentoImagens _imagens;
    private readonly IRelogio _relogio;
    private IdentidadeLoja? _cache;

    public IdentidadeLojaService(IAppDbContext db, ILicencaLoja licenca, IArmazenamentoImagens imagens, IRelogio relogio)
    {
        _db = db;
        _licenca = licenca;
        _imagens = imagens;
        _relogio = relogio;
    }

    /// <summary>Lida uma vez por requisição (o serviço é scoped): o layout e as páginas usam o mesmo objeto.</summary>
    public async Task<IdentidadeLoja> ObterAsync(CancellationToken ct = default)
    {
        if (_cache is not null) return _cache;

        var v = await _db.Configuracoes.AsNoTracking()
            .Where(c => c.Chave.StartsWith(Prefixo) || c.Chave.StartsWith("Vitrine."))
            .ToDictionaryAsync(c => c.Chave, c => c.Valor, ct);
        string? Ler(string chave) => v.TryGetValue(chave, out var x) && !string.IsNullOrWhiteSpace(x) ? x : null;

        var estado = _licenca.Estado;
        var dados = estado.Ativa ? estado.Dados : null;
        var padrao = new IdentidadeLoja { NomeLoja = "", RazaoSocial = "", Cnpj = "", Licenca = estado.Situacao };

        _cache = new IdentidadeLoja
        {
            NomeLoja = dados?.NomeLoja ?? NomeDemonstracao,
            RazaoSocial = dados?.RazaoSocial ?? NomeDemonstracao,
            Cnpj = dados?.Cnpj ?? CnpjDemonstracao,
            Licenca = estado.Situacao,
            Logo = Ler(ChaveLogo),
            CorPrincipal = Ler("Loja.CorPrincipal") is { } c1 && Cores.EhHex(c1) ? c1 : CorPrincipalPadrao,
            CorDestaque = Ler("Loja.CorDestaque") is { } c2 && Cores.EhHex(c2) ? c2 : CorDestaquePadrao,
            WhatsApp = Ler("Loja.WhatsApp"),
            Telefone = Ler("Loja.Telefone"),
            Email = Ler("Loja.Email"),
            Endereco = Ler("Loja.Endereco"),
            Horario = Ler("Loja.Horario"),
            Instagram = Ler("Loja.Instagram"),
            Facebook = Ler("Loja.Facebook"),
            Youtube = Ler("Loja.Youtube"),
            AvisoTopo = Ler("Loja.AvisoTopo"),
            BannerSelo = Ler("Vitrine.BannerSelo") ?? padrao.BannerSelo,
            BannerTitulo = Ler("Vitrine.BannerTitulo") ?? padrao.BannerTitulo,
            BannerTexto = Ler("Vitrine.BannerTexto") ?? padrao.BannerTexto,
            BannerBotao = Ler("Vitrine.BannerBotao") ?? padrao.BannerBotao,
            BannerLink = Ler("Vitrine.BannerLink") ?? padrao.BannerLink
        };
        return _cache;
    }

    /// <summary>Para o layout: se o banco estiver fora (página de erro), usa a licença e as cores padrão em vez de falhar.</summary>
    public async Task<IdentidadeLoja> ObterOuPadraoAsync(CancellationToken ct = default)
    {
        try
        {
            return await ObterAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var estado = _licenca.Estado;
            var dados = estado.Ativa ? estado.Dados : null;
            return new IdentidadeLoja
            {
                NomeLoja = dados?.NomeLoja ?? NomeDemonstracao,
                RazaoSocial = dados?.RazaoSocial ?? NomeDemonstracao,
                Cnpj = dados?.Cnpj ?? CnpjDemonstracao,
                Licenca = estado.Situacao
            };
        }
    }

    public async Task<Resultado> SalvarAsync(IdentidadeEditavel e, string autor, CancellationToken ct = default)
    {
        var corPrincipal = Cores.Normalizar(e.CorPrincipal ?? "");
        var corDestaque = Cores.Normalizar(e.CorDestaque ?? "");
        if (!Cores.EhHex(corPrincipal) || !Cores.EhHex(corDestaque)) return Resultado.Falha("Informe as cores no formato #RRGGBB.");
        // Acessibilidade (WCAG AA): texto branco sobre a cor principal e texto na cor principal sobre a cor de destaque
        if (Cores.Contraste(corPrincipal, "#FFFFFF") < 4.5)
            return Resultado.Falha("A cor principal está clara demais: o texto branco sobre ela fica ilegível. Escolha um tom mais escuro.");
        if (Cores.Contraste(corPrincipal, corDestaque) < 4.5)
            return Resultado.Falha("A cor de destaque precisa contrastar com a cor principal (o texto dos botões usa a cor principal). Escolha um tom mais claro.");

        var email = Limpar(e.Email, 254);
        if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _)) return Resultado.Falha("E-mail de atendimento inválido.");
        var whatsapp = Limpar(e.WhatsApp, 20);
        if (whatsapp is not null && !Telefone.EhValido(whatsapp)) return Resultado.Falha("WhatsApp inválido: informe DDD e número.");
        var telefone = Limpar(e.Telefone, 20);
        if (telefone is not null && !Telefone.EhValido(telefone)) return Resultado.Falha("Telefone inválido: informe DDD e número.");

        foreach (var (rotulo, url) in new[] { ("Instagram", e.Instagram), ("Facebook", e.Facebook), ("YouTube", e.Youtube) })
            if (!string.IsNullOrWhiteSpace(url) && !UrlSegura(url, permitirRelativa: false))
                return Resultado.Falha($"Link do {rotulo} inválido: use o endereço completo começando com https://");
        if (!string.IsNullOrWhiteSpace(e.BannerLink) && !UrlSegura(e.BannerLink, permitirRelativa: true))
            return Resultado.Falha("Link do banner inválido: use um caminho da loja (ex.: /categoria/casa) ou um endereço https://");

        var valores = new Dictionary<string, string?>
        {
            ["Loja.CorPrincipal"] = corPrincipal,
            ["Loja.CorDestaque"] = corDestaque,
            ["Loja.WhatsApp"] = whatsapp is null ? null : Telefone.Formatar(whatsapp),
            ["Loja.Telefone"] = telefone is null ? null : Telefone.Formatar(telefone),
            ["Loja.Email"] = email,
            ["Loja.Endereco"] = Limpar(e.Endereco, 300),
            ["Loja.Horario"] = Limpar(e.Horario, 120),
            ["Loja.Instagram"] = Limpar(e.Instagram, 300),
            ["Loja.Facebook"] = Limpar(e.Facebook, 300),
            ["Loja.Youtube"] = Limpar(e.Youtube, 300),
            ["Loja.AvisoTopo"] = Limpar(e.AvisoTopo, 160),
            ["Vitrine.BannerSelo"] = Limpar(e.BannerSelo, 40),
            ["Vitrine.BannerTitulo"] = Limpar(e.BannerTitulo, 90),
            ["Vitrine.BannerTexto"] = Limpar(e.BannerTexto, 200),
            ["Vitrine.BannerBotao"] = Limpar(e.BannerBotao, 30),
            ["Vitrine.BannerLink"] = Limpar(e.BannerLink, 300)
        };
        await GravarAsync(valores, autor, ct);
        return Resultado.Ok();
    }

    /// <summary>Logo: JPG, PNG ou WebP até 2 MB (mesma validação das fotos de produto).</summary>
    public async Task<Resultado> SalvarLogoAsync(byte[] conteudo, string nomeArquivo, string autor, CancellationToken ct = default)
    {
        var erro = ValidadorImagem.Validar(conteudo, nomeArquivo, out var extensao);
        if (erro is not null) return Resultado.Falha(erro);

        var anterior = (await ObterAsync(ct)).Logo;
        var arquivo = await _imagens.SalvarAsync(conteudo, extensao!, ct);
        await GravarAsync(new Dictionary<string, string?> { [ChaveLogo] = arquivo }, autor, ct);
        if (!string.IsNullOrEmpty(anterior)) await _imagens.RemoverAsync(anterior, ct);
        return Resultado.Ok();
    }

    public async Task RemoverLogoAsync(string autor, CancellationToken ct = default)
    {
        var anterior = (await ObterAsync(ct)).Logo;
        await GravarAsync(new Dictionary<string, string?> { [ChaveLogo] = null }, autor, ct);
        if (!string.IsNullOrEmpty(anterior)) await _imagens.RemoverAsync(anterior, ct);
    }

    private async Task GravarAsync(Dictionary<string, string?> valores, string autor, CancellationToken ct)
    {
        var chaves = valores.Keys.ToList();
        var existentes = await _db.Configuracoes.Where(c => chaves.Contains(c.Chave)).ToDictionaryAsync(c => c.Chave, ct);
        foreach (var (chave, valor) in valores)
        {
            if (existentes.TryGetValue(chave, out var atual))
            {
                if (valor is null)
                {
                    _db.Configuracoes.Remove(atual);
                    continue;
                }
                if (atual.Valor == valor) continue;
                atual.Valor = valor;
                atual.AlteradoEm = _relogio.AgoraUtc;
                atual.AlteradoPor = autor;
            }
            else if (valor is not null)
            {
                _db.Configuracoes.Add(new Configuracao { Chave = chave, Valor = valor, AlteradoEm = _relogio.AgoraUtc, AlteradoPor = autor });
            }
        }
        await _db.SaveChangesAsync(ct);
        _cache = null;
    }

    private static string? Limpar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        return t.Length > maximo ? t[..maximo] : t;
    }

    /// <summary>Só https:// ou caminho interno ("/..."); bloqueia javascript:, data: e "//outro-site".</summary>
    internal static bool UrlSegura(string url, bool permitirRelativa)
    {
        var u = url.Trim();
        if (permitirRelativa && u.StartsWith('/') && !u.StartsWith("//", StringComparison.Ordinal) && !u.Contains('\\'))
            return true;
        return Uri.TryCreate(u, UriKind.Absolute, out var abs) && abs.Scheme == Uri.UriSchemeHttps;
    }
}
