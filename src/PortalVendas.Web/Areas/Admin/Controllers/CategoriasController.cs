using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Admin;

namespace PortalVendas.Web.Areas.Admin.Controllers;

public class CategoriasController : AdminControllerBase
{
    private readonly CategoriaAdminService _categorias;

    public CategoriasController(CategoriaAdminService categorias) => _categorias = categorias;

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _categorias.ListarAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Salvar(int id, string nome, bool ativa, CancellationToken ct)
    {
        var r = await _categorias.SalvarAsync(id, nome, ativa, ct);
        if (r.Sucesso) Sucesso("Categoria salva."); else Erro(r.Erro);
        return RedirectToAction(nameof(Index));
    }
}
