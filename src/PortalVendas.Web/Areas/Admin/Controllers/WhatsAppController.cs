using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PortalVendas.Application.WhatsApp;
using PortalVendas.Infrastructure.Servicos;
using PortalVendas.Web.Areas.Admin.Models;

namespace PortalVendas.Web.Areas.Admin.Controllers;

/// <summary>Atendimento pelo WhatsApp: conversas, resposta do atendente e configuração (módulo pago da licença).</summary>
public class WhatsAppController : AdminControllerBase
{
    private readonly ConversasWhatsAppService _conversas;
    private readonly ConfiguracaoWhatsAppService _configuracao;
    private readonly IFabricaConectorWhatsApp _fabrica;
    private readonly LojaOptions _loja;

    public WhatsAppController(ConversasWhatsAppService conversas, ConfiguracaoWhatsAppService configuracao, IFabricaConectorWhatsApp fabrica,
        IOptions<LojaOptions> loja)
    {
        _conversas = conversas;
        _configuracao = configuracao;
        _fabrica = fabrica;
        _loja = loja.Value;
    }

    public async Task<IActionResult> Index(bool atendente = false, string? termo = null, int pagina = 1, CancellationToken ct = default)
    {
        if (!_configuracao.Licenciado) return View("SemModulo");
        var config = await _configuracao.ObterAsync(ct);
        return View(new WhatsAppIndexViewModel
        {
            Conversas = await _conversas.ListarAsync(atendente, termo, pagina, ct),
            SomenteAtendente = atendente,
            Termo = termo,
            Configurado = config.Provedor != ProvedorWhatsApp.Desativado,
            RoboAtivo = config.RoboAtivo
        });
    }

    public async Task<IActionResult> Conversa(int id, CancellationToken ct)
    {
        if (!_configuracao.Licenciado) return View("SemModulo");
        var detalhe = await _conversas.AbrirAsync(id, ct);
        return detalhe is null ? NotFound() : View(detalhe);
    }

    [HttpPost]
    public async Task<IActionResult> Responder(int id, string? texto, CancellationToken ct)
    {
        var r = await _conversas.ResponderAsync(id, texto ?? "", Autor, ct);
        if (!r.Sucesso) Erro(r.Erro);
        return RedirectToAction(nameof(Conversa), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Assumir(int id, CancellationToken ct)
    {
        var r = await _conversas.AssumirAsync(id, ct);
        if (r.Sucesso) Sucesso("Você assumiu a conversa: o robô não responde até ela ser devolvida."); else Erro(r.Erro);
        return RedirectToAction(nameof(Conversa), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Devolver(int id, bool avisar, CancellationToken ct)
    {
        var r = await _conversas.DevolverAoRoboAsync(id, avisar, ct);
        if (r.Sucesso) Sucesso("Conversa devolvida ao robô."); else Erro(r.Erro);
        return RedirectToAction(nameof(Conversa), new { id });
    }

    public async Task<IActionResult> Configuracao(CancellationToken ct)
    {
        if (!_configuracao.Licenciado) return View("SemModulo");
        return View(await MontarConfiguracaoAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Configuracao(ConfiguracaoWhatsAppInput e, CancellationToken ct)
    {
        var r = await _configuracao.SalvarAsync(new EdicaoConfiguracaoWhatsApp(
            e.Provedor, e.RoboAtivo, e.BoasVindas,
            e.MetaPhoneNumberId, e.MetaToken, e.MetaAppSecret, e.MetaModeloAviso, e.MetaIdiomaModelo,
            e.EvolutionUrl, e.EvolutionInstancia, e.EvolutionApiKey,
            e.IaAtiva, e.IaChaveApi, e.IaModelo), Autor, ct);
        if (r.Sucesso) Sucesso("Configuração do WhatsApp salva."); else Erro(r.Erro);
        return RedirectToAction(nameof(Configuracao));
    }

    /// <summary>Envia uma mensagem de teste. Na Meta usa o modelo hello_world (o único liberado sem conversa aberta).</summary>
    [HttpPost]
    public async Task<IActionResult> Testar(string? telefone, CancellationToken ct)
    {
        var numero = TelefoneWhatsApp.Normalizar(telefone);
        if (numero.Length < 12)
        {
            Erro("Informe o celular com DDD (ex.: 19 99999-0000).");
            return RedirectToAction(nameof(Configuracao));
        }
        var conector = await _fabrica.CriarAsync(ct);
        if (conector is null)
        {
            Erro("Salve a configuração do provedor antes de testar.");
            return RedirectToAction(nameof(Configuracao));
        }
        var r = conector.Provedor == ProvedorWhatsApp.Meta
            ? await conector.EnviarModeloAsync(numero, "hello_world", "en_US", Array.Empty<string>(), "", ct)
            : await conector.EnviarAsync(numero, MensagemSaida.Simples("✅ Teste do atendimento pelo WhatsApp: a conexão está funcionando."), ct);
        if (r.Sucesso) Sucesso($"Mensagem de teste enviada para {TelefoneWhatsApp.Formatar(numero)}.");
        else Erro("O provedor recusou o teste: " + r.Erro);
        return RedirectToAction(nameof(Configuracao));
    }

    [HttpPost]
    public async Task<IActionResult> RenovarToken(CancellationToken ct)
    {
        await _configuracao.RenovarTokenEvolutionAsync(Autor, ct);
        Sucesso("Novo endereço gerado. Atualize o webhook na Evolution API: o endereço antigo parou de funcionar.");
        return RedirectToAction(nameof(Configuracao));
    }

    private async Task<WhatsAppConfiguracaoViewModel> MontarConfiguracaoAsync(CancellationToken ct)
    {
        var c = await _configuracao.ObterAsync(ct);
        var urlBase = _loja.UrlBase.TrimEnd('/');
        return new WhatsAppConfiguracaoViewModel
        {
            Provedor = c.Provedor,
            RoboAtivo = c.RoboAtivo,
            BoasVindas = c.BoasVindas,
            MetaPhoneNumberId = c.MetaPhoneNumberId,
            MetaTemToken = !string.IsNullOrEmpty(c.MetaToken),
            MetaTemAppSecret = !string.IsNullOrEmpty(c.MetaAppSecret),
            MetaModeloAviso = c.MetaModeloAviso,
            MetaIdiomaModelo = c.MetaIdiomaModelo,
            EvolutionUrl = c.EvolutionUrl,
            EvolutionInstancia = c.EvolutionInstancia,
            EvolutionTemApiKey = !string.IsNullOrEmpty(c.EvolutionApiKey),
            IaAtiva = c.IaAtiva,
            IaTemChave = !string.IsNullOrEmpty(c.IaChaveApi),
            IaModelo = c.IaModelo,
            UrlWebhookMeta = urlBase + "/webhooks/whatsapp/meta",
            MetaVerifyToken = c.MetaVerifyToken,
            UrlWebhookEvolution = urlBase + "/webhooks/whatsapp/evolution/" + c.EvolutionTokenWebhook
        };
    }
}
