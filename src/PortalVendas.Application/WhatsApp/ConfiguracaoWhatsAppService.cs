using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Domain.Frete;

namespace PortalVendas.Application.WhatsApp;

/// <summary>Configuração do WhatsApp com os segredos já decifrados. Uso interno (nunca vai para a tela).</summary>
public sealed record ConfiguracaoWhatsApp
{
    public ProvedorWhatsApp Provedor { get; init; }
    /// <summary>Robô ligado. Desligado, as mensagens só ficam no painel para o atendente.</summary>
    public bool RoboAtivo { get; init; } = true;
    public string? BoasVindas { get; init; }

    public string? MetaPhoneNumberId { get; init; }
    public string? MetaToken { get; init; }
    public string? MetaAppSecret { get; init; }
    public string MetaVerifyToken { get; init; } = string.Empty;
    public string? MetaModeloAviso { get; init; }
    public string MetaIdiomaModelo { get; init; } = "pt_BR";

    public string? EvolutionUrl { get; init; }
    public string? EvolutionInstancia { get; init; }
    public string? EvolutionApiKey { get; init; }
    public string EvolutionTokenWebhook { get; init; } = string.Empty;

    public bool IaAtiva { get; init; }
    public string? IaChaveApi { get; init; }
    public string IaModelo { get; init; } = ConfiguracaoWhatsAppService.ModeloIaPadrao;

    public bool MetaCompleta => !string.IsNullOrWhiteSpace(MetaPhoneNumberId) && !string.IsNullOrWhiteSpace(MetaToken);
    public bool EvolutionCompleta => !string.IsNullOrWhiteSpace(EvolutionUrl) && !string.IsNullOrWhiteSpace(EvolutionInstancia)
                                     && !string.IsNullOrWhiteSpace(EvolutionApiKey);
    public bool IaPronta => IaAtiva && !string.IsNullOrWhiteSpace(IaChaveApi);
}

/// <summary>Campos do formulário do painel. Segredo vazio = manter o que está salvo.</summary>
public sealed record EdicaoConfiguracaoWhatsApp(
    ProvedorWhatsApp Provedor, bool RoboAtivo, string? BoasVindas,
    string? MetaPhoneNumberId, string? MetaToken, string? MetaAppSecret, string? MetaModeloAviso, string? MetaIdiomaModelo,
    string? EvolutionUrl, string? EvolutionInstancia, string? EvolutionApiKey,
    bool IaAtiva, string? IaChaveApi, string? IaModelo);

/// <summary>Lê e grava a configuração do WhatsApp na tabela Configuracao. Tokens e chaves ficam cifrados.</summary>
public sealed class ConfiguracaoWhatsAppService
{
    public const string ModeloIaPadrao = "claude-haiku-5-5";
    private const string Prefixo = "WhatsApp.";

    private readonly IAppDbContext _db;
    private readonly IProtetorDados _protetor;
    private readonly ILicencaLoja _licenca;
    private readonly IRelogio _relogio;

    public ConfiguracaoWhatsAppService(IAppDbContext db, IProtetorDados protetor, ILicencaLoja licenca, IRelogio relogio)
    {
        _db = db;
        _protetor = protetor;
        _licenca = licenca;
        _relogio = relogio;
    }

    /// <summary>O módulo WhatsApp está na licença desta loja.</summary>
    public bool Licenciado => _licenca.Estado.TemModulo(ModulosLicenca.WhatsApp);

    public async Task<ConfiguracaoWhatsApp> ObterAsync(CancellationToken ct = default)
    {
        var v = await _db.Configuracoes.AsNoTracking()
            .Where(c => c.Chave.StartsWith(Prefixo))
            .ToDictionaryAsync(c => c.Chave, c => c.Valor, ct);
        string? Ler(string chave) => v.TryGetValue(Prefixo + chave, out var x) && !string.IsNullOrWhiteSpace(x) ? x : null;
        string? Segredo(string chave) => Ler(chave) is { } cifrado ? _protetor.Desproteger(cifrado) : null;
        bool Bool(string chave, bool padrao) => Ler(chave) is { } b ? b == "1" : padrao;

        var provedor = byte.TryParse(Ler("Provedor"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
                       && Enum.IsDefined(typeof(ProvedorWhatsApp), p) ? (ProvedorWhatsApp)p : ProvedorWhatsApp.Desativado;

        var config = new ConfiguracaoWhatsApp
        {
            Provedor = provedor,
            RoboAtivo = Bool("RoboAtivo", true),
            BoasVindas = Ler("BoasVindas"),
            MetaPhoneNumberId = Ler("Meta.PhoneNumberId"),
            MetaToken = Segredo("Meta.Token"),
            MetaAppSecret = Segredo("Meta.AppSecret"),
            MetaVerifyToken = Ler("Meta.VerifyToken") ?? string.Empty,
            MetaModeloAviso = Ler("Meta.ModeloAviso"),
            MetaIdiomaModelo = Ler("Meta.IdiomaModelo") ?? "pt_BR",
            EvolutionUrl = Ler("Evolution.Url"),
            EvolutionInstancia = Ler("Evolution.Instancia"),
            EvolutionApiKey = Segredo("Evolution.ApiKey"),
            EvolutionTokenWebhook = Ler("Evolution.TokenWebhook") ?? string.Empty,
            IaAtiva = Bool("IA.Ativa", false),
            IaChaveApi = Segredo("IA.ChaveApi"),
            IaModelo = Ler("IA.Modelo") ?? ModeloIaPadrao
        };

        // Tokens de verificação dos webhooks: gerados uma vez e guardados
        if (string.IsNullOrEmpty(config.MetaVerifyToken) || string.IsNullOrEmpty(config.EvolutionTokenWebhook))
        {
            config = config with
            {
                MetaVerifyToken = string.IsNullOrEmpty(config.MetaVerifyToken) ? NovoToken() : config.MetaVerifyToken,
                EvolutionTokenWebhook = string.IsNullOrEmpty(config.EvolutionTokenWebhook) ? NovoToken() : config.EvolutionTokenWebhook
            };
            try
            {
                await GravarAsync(new Dictionary<string, string?>
                {
                    ["Meta.VerifyToken"] = config.MetaVerifyToken,
                    ["Evolution.TokenWebhook"] = config.EvolutionTokenWebhook
                }, "sistema", ct);
            }
            catch (DbUpdateException)
            {
                // Outra requisição gerou os tokens ao mesmo tempo: vale o que ficou gravado
                _db.ChangeTracker.Clear();
                return await ObterAsync(ct);
            }
        }
        return config;
    }

    public async Task<Resultado> SalvarAsync(EdicaoConfiguracaoWhatsApp e, string autor, CancellationToken ct = default)
    {
        if (!Licenciado) return Resultado.Falha("O módulo WhatsApp não está incluído na licença desta loja.");
        if (!Enum.IsDefined(e.Provedor)) return Resultado.Falha("Escolha o provedor do WhatsApp.");

        var atual = await ObterAsync(ct);
        var evolutionUrl = Limpar(e.EvolutionUrl, 300)?.TrimEnd('/');
        if (evolutionUrl is not null && !(Uri.TryCreate(evolutionUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)))
            return Resultado.Falha("Endereço da Evolution API inválido (ex.: https://evolution.minhaloja.com.br).");
        var phoneId = Limpar(e.MetaPhoneNumberId, 40);
        if (phoneId is not null && !phoneId.All(char.IsAsciiDigit)) return Resultado.Falha("O Phone Number ID da Meta tem só números.");
        var instancia = Limpar(e.EvolutionInstancia, 100);
        if (instancia is not null && instancia.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return Resultado.Falha("Nome da instância da Evolution: use só letras, números, - e _.");
        var modelo = Limpar(e.MetaModeloAviso, 100);
        if (modelo is not null && modelo.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')))
            return Resultado.Falha("Nome do modelo da Meta: letras minúsculas, números e _ (como aparece no Gerenciador do WhatsApp).");

        var proxima = atual with
        {
            Provedor = e.Provedor,
            MetaPhoneNumberId = phoneId,
            MetaToken = Limpar(e.MetaToken, 1000) ?? atual.MetaToken,
            EvolutionUrl = evolutionUrl,
            EvolutionInstancia = instancia,
            EvolutionApiKey = Limpar(e.EvolutionApiKey, 300) ?? atual.EvolutionApiKey
        };
        if (proxima.Provedor == ProvedorWhatsApp.Meta && !proxima.MetaCompleta)
            return Resultado.Falha("Para a API oficial informe o Phone Number ID e o token de acesso.");
        if (proxima.Provedor == ProvedorWhatsApp.Meta && string.IsNullOrWhiteSpace(e.MetaAppSecret) && string.IsNullOrWhiteSpace(atual.MetaAppSecret))
            return Resultado.Falha("Informe o App Secret do aplicativo da Meta: sem ele não dá para conferir que as mensagens vêm mesmo do WhatsApp.");
        if (proxima.Provedor == ProvedorWhatsApp.Evolution && !proxima.EvolutionCompleta)
            return Resultado.Falha("Para a Evolution API informe o endereço, a instância e a API key.");
        if (e.IaAtiva && string.IsNullOrWhiteSpace(e.IaChaveApi) && string.IsNullOrWhiteSpace(atual.IaChaveApi))
            return Resultado.Falha("Para ligar a IA informe a chave da API da Anthropic.");

        var valores = new Dictionary<string, string?>
        {
            ["Provedor"] = ((byte)e.Provedor).ToString(CultureInfo.InvariantCulture),
            ["RoboAtivo"] = e.RoboAtivo ? "1" : "0",
            ["BoasVindas"] = Limpar(e.BoasVindas, 500),
            ["Meta.PhoneNumberId"] = phoneId,
            ["Meta.ModeloAviso"] = modelo,
            ["Meta.IdiomaModelo"] = Limpar(e.MetaIdiomaModelo, 10) ?? "pt_BR",
            ["Evolution.Url"] = evolutionUrl,
            ["Evolution.Instancia"] = instancia,
            ["IA.Ativa"] = e.IaAtiva ? "1" : "0",
            ["IA.Modelo"] = Limpar(e.IaModelo, 60) ?? ModeloIaPadrao
        };
        // Segredos: só regrava quando um valor novo foi digitado
        if (Limpar(e.MetaToken, 1000) is { } token) valores["Meta.Token"] = _protetor.Proteger(token);
        if (Limpar(e.MetaAppSecret, 200) is { } segredo) valores["Meta.AppSecret"] = _protetor.Proteger(segredo);
        if (Limpar(e.EvolutionApiKey, 300) is { } apiKey) valores["Evolution.ApiKey"] = _protetor.Proteger(apiKey);
        if (Limpar(e.IaChaveApi, 300) is { } chaveIa) valores["IA.ChaveApi"] = _protetor.Proteger(chaveIa);

        await GravarAsync(valores, autor, ct);
        return Resultado.Ok();
    }

    /// <summary>Gera um novo token para a URL do webhook da Evolution (o antigo deixa de funcionar).</summary>
    public Task RenovarTokenEvolutionAsync(string autor, CancellationToken ct = default) =>
        GravarAsync(new Dictionary<string, string?> { ["Evolution.TokenWebhook"] = NovoToken() }, autor, ct);

    private async Task GravarAsync(Dictionary<string, string?> valores, string autor, CancellationToken ct)
    {
        var chaves = valores.Keys.Select(k => Prefixo + k).ToList();
        var existentes = await _db.Configuracoes.Where(c => chaves.Contains(c.Chave)).ToDictionaryAsync(c => c.Chave, ct);
        var agora = _relogio.AgoraUtc;
        foreach (var (k, valor) in valores)
        {
            var chave = Prefixo + k;
            if (existentes.TryGetValue(chave, out var c))
            {
                if (valor is null) { _db.Configuracoes.Remove(c); continue; }
                if (c.Valor == valor) continue;
                c.Valor = valor;
                c.AlteradoEm = agora;
                c.AlteradoPor = autor;
            }
            else if (valor is not null)
            {
                _db.Configuracoes.Add(new Configuracao { Chave = chave, Valor = valor, AlteradoEm = agora, AlteradoPor = autor });
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string NovoToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();

    private static string? Limpar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        return t.Length > maximo ? t[..maximo] : t;
    }
}
