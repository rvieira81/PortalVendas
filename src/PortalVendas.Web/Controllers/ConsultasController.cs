using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Catalogo;
using PortalVendas.Application.Frete;
using PortalVendas.Web.Infra;

namespace PortalVendas.Web.Controllers;

[Route("api")]
[ApiController]
public class ConsultasController : ControllerBase
{
    private readonly ICepService _cep;
    private readonly CatalogoService _catalogo;
    private readonly FreteService _frete;

    public ConsultasController(ICepService cep, CatalogoService catalogo, FreteService frete)
    {
        _cep = cep;
        _catalogo = catalogo;
        _frete = frete;
    }

    /// <summary>RF07: preenchimento automático do endereço.</summary>
    [HttpGet("cep/{cep}")]
    [EnableRateLimiting("consulta")]
    public async Task<IActionResult> Cep(string cep, CancellationToken ct)
    {
        var endereco = await _cep.ConsultarAsync(cep, ct);
        return endereco is null ? NotFound(new { mensagem = "CEP não encontrado." }) : Ok(endereco);
    }

    /// <summary>Calculadora de frete da página do produto (mesma cotação usada no checkout).</summary>
    [HttpGet("frete")]
    [EnableRateLimiting("consulta")]
    public async Task<IActionResult> Frete(int produtoId, string? cep, int quantidade = 1, CancellationToken ct = default)
    {
        var cepNormalizado = PortalVendas.Domain.Comum.Cep.Normalizar(cep ?? "");
        if (cepNormalizado.Length != 8) return BadRequest(new { mensagem = "Informe um CEP com 8 números." });

        var item = await _catalogo.ObterItemFreteAsync(produtoId, Math.Clamp(quantidade, 1, 99), ct);
        if (item is null) return NotFound(new { mensagem = "Produto indisponível." });

        var cotacao = await _frete.CotarAsync(cepNormalizado, new[] { item }, ct);
        if (!cotacao.Sucesso) return UnprocessableEntity(new { mensagem = cotacao.Erro });

        return Ok(cotacao.Valor!.Select(o => new
        {
            descricao = o.Descricao,
            valor = o.Valor,
            valorTexto = o.Gratis ? "Grátis" : Formatacao.Moeda(o.Valor),
            prazo = Formatacao.Prazo(o.PrazoDias)
        }));
    }
}
