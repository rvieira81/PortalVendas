using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortalVendas.Web.Infra;

namespace PortalVendas.Web.Areas.Admin.Controllers;

/// <summary>RNF05: toda a área exige perfil Administrador autenticado com segundo fator.</summary>
[Area("Admin")]
[Authorize(Policy = Politicas.Admin)]
public abstract class AdminControllerBase : Controller
{
    protected string Autor => User.Autor();

    protected void Sucesso(string mensagem) => TempData["Sucesso"] = mensagem;
    protected void Erro(string? mensagem) => TempData["Erro"] = mensagem;
}
