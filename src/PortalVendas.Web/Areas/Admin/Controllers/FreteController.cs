using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Admin;
using PortalVendas.Domain.Frete;
using PortalVendas.Web.Areas.Admin.Models;
using PortalVendas.Web.Infra;

namespace PortalVendas.Web.Areas.Admin.Controllers;

/// <summary>RF19: origem, manuseio, frete grátis, parcelamento e tabela de contingência.</summary>
public class FreteController : AdminControllerBase
{
    private readonly ConfiguracaoLojaService _configuracao;
    private readonly FaixaFreteService _faixas;

    public FreteController(ConfiguracaoLojaService configuracao, FaixaFreteService faixas)
    {
        _configuracao = configuracao;
        _faixas = faixas;
    }

    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new FreteAdminViewModel
        {
            Configuracao = await _configuracao.ObterAsync(ct),
            Faixas = await _faixas.ListarAsync(ct)
        });

    [HttpPost]
    public async Task<IActionResult> SalvarConfiguracao(ConfiguracaoInput entrada, CancellationToken ct)
    {
        Formatacao.TentarLerDecimal(entrada.FreteGratisAPartirDe, out var gratis);
        if (!Formatacao.TentarLerDecimal(entrada.ParcelaMinima, out var parcelaMinima)) parcelaMinima = 0;

        var r = await _configuracao.SalvarAsync(new ConfiguracaoLoja(
            entrada.CepOrigem, entrada.DiasManuseio, gratis, entrada.ParcelasMaximas, parcelaMinima), Autor, ct);
        if (r.Sucesso) Sucesso("Configurações salvas."); else Erro(r.Erro);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SalvarFaixa(FaixaInput entrada, CancellationToken ct)
    {
        if (!Formatacao.TentarLerDecimal(entrada.Valor, out var valor))
        {
            Erro("Valor inválido.");
            return RedirectToAction(nameof(Index));
        }
        var r = await _faixas.SalvarAsync(new FaixaFrete
        {
            Id = entrada.Id,
            Descricao = entrada.Descricao,
            CepInicio = entrada.CepInicio,
            CepFim = entrada.CepFim,
            Valor = valor,
            PrazoDias = entrada.PrazoDias,
            Ativa = entrada.Ativa
        }, ct);
        if (r.Sucesso) Sucesso("Faixa salva."); else Erro(r.Erro);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ExcluirFaixa(int id, CancellationToken ct)
    {
        await _faixas.ExcluirAsync(id, ct);
        Sucesso("Faixa excluída.");
        return RedirectToAction(nameof(Index));
    }
}
