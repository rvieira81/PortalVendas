using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Web.Areas.Admin.Models;

namespace PortalVendas.Web.Areas.Admin.Controllers;

/// <summary>
/// Identidade da loja. Nome, razão social e CNPJ vêm da licença e aparecem só para leitura:
/// para trocá-los, o fornecedor emite outra licença. Logo, cores, contatos e banner o lojista altera aqui.
/// </summary>
public class IdentidadeController : AdminControllerBase
{
    private const int TamanhoMaximoLicenca = 64 * 1024;

    private readonly IdentidadeLojaService _identidade;
    private readonly ILicencaLoja _licenca;
    private readonly ILogger<IdentidadeController> _logger;

    public IdentidadeController(IdentidadeLojaService identidade, ILicencaLoja licenca, ILogger<IdentidadeController> logger)
    {
        _identidade = identidade;
        _licenca = licenca;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new IdentidadeAdminViewModel
        {
            Identidade = await _identidade.ObterAsync(ct),
            Licenca = _licenca.Estado
        });

    [HttpPost]
    public async Task<IActionResult> Salvar(IdentidadeInput e, CancellationToken ct)
    {
        var r = await _identidade.SalvarAsync(new IdentidadeEditavel(
            e.CorPrincipal, e.CorDestaque, e.WhatsApp, e.Telefone, e.Email, e.Endereco, e.Horario,
            e.Instagram, e.Facebook, e.Youtube, e.AvisoTopo,
            e.BannerSelo, e.BannerTitulo, e.BannerTexto, e.BannerBotao, e.BannerLink), Autor, ct);
        if (r.Sucesso) Sucesso("Identidade da loja salva."); else Erro(r.Erro);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> EnviarLogo(IFormFile? arquivo, CancellationToken ct)
    {
        if (arquivo is null || arquivo.Length == 0)
        {
            Erro("Escolha o arquivo do logo.");
            return RedirectToAction(nameof(Index));
        }
        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, ct);
        var r = await _identidade.SalvarLogoAsync(memoria.ToArray(), arquivo.FileName, Autor, ct);
        if (r.Sucesso) Sucesso("Logo atualizado."); else Erro(r.Erro);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> RemoverLogo(CancellationToken ct)
    {
        await _identidade.RemoverLogoAsync(Autor, ct);
        Sucesso("Logo removido: a loja volta a usar a inicial do nome.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Instala o arquivo licenca.lic enviado pelo fornecedor (assinado digitalmente; não dá para editar).</summary>
    [HttpPost]
    [RequestSizeLimit(TamanhoMaximoLicenca + 16 * 1024)]
    public async Task<IActionResult> InstalarLicenca(IFormFile? arquivo, CancellationToken ct)
    {
        if (arquivo is null || arquivo.Length == 0 || arquivo.Length > TamanhoMaximoLicenca)
        {
            Erro("Escolha o arquivo licenca.lic enviado pelo fornecedor.");
            return RedirectToAction(nameof(Index));
        }
        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, ct);
        var r = await _licenca.InstalarAsync(memoria.ToArray(), ct);
        if (r.Sucesso)
        {
            _logger.LogInformation("Licença instalada por {Autor}: {Mensagem}", Autor, _licenca.Estado.Mensagem);
            Sucesso("Licença instalada. Nome, razão social e CNPJ foram atualizados.");
        }
        else
        {
            _logger.LogWarning("Licença recusada (enviada por {Autor}): {Erro}", Autor, r.Erro);
            Erro(r.Erro);
        }
        return RedirectToAction(nameof(Index));
    }
}
