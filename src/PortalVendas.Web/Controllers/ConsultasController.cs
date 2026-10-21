using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PortalVendas.Application.Abstracoes;

namespace PortalVendas.Web.Controllers;

[Route("api")]
[ApiController]
public class ConsultasController : ControllerBase
{
    private readonly ICepService _cep;

    public ConsultasController(ICepService cep) => _cep = cep;

    /// <summary>RF07: preenchimento automático do endereço.</summary>
    [HttpGet("cep/{cep}")]
    [EnableRateLimiting("consulta")]
    public async Task<IActionResult> Cep(string cep, CancellationToken ct)
    {
        var endereco = await _cep.ConsultarAsync(cep, ct);
        return endereco is null ? NotFound(new { mensagem = "CEP não encontrado." }) : Ok(endereco);
    }
}
