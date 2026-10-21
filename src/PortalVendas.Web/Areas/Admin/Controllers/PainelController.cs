using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Admin;

namespace PortalVendas.Web.Areas.Admin.Controllers;

public class PainelController : AdminControllerBase
{
    private readonly DashboardService _dashboard;

    public PainelController(DashboardService dashboard) => _dashboard = dashboard;

    /// <summary>RF20</summary>
    public async Task<IActionResult> Index(DateOnly? de, DateOnly? ate, CancellationToken ct) =>
        View(await _dashboard.ObterAsync(de, ate, ct));
}
