using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Domain.Catalogo;
using PortalVendas.Domain.Comum;

namespace PortalVendas.Application.Admin;

public record CategoriaAdminResumo(int Id, string Nome, string Slug, bool Ativa, int Produtos);

public class CategoriaAdminService
{
    private readonly IAppDbContext _db;
    private readonly IRelogio _relogio;

    public CategoriaAdminService(IAppDbContext db, IRelogio relogio)
    {
        _db = db;
        _relogio = relogio;
    }

    public Task<List<CategoriaAdminResumo>> ListarAsync(CancellationToken ct = default) =>
        _db.Categorias.AsNoTracking()
            .OrderBy(c => c.Nome)
            .Select(c => new CategoriaAdminResumo(c.Id, c.Nome, c.Slug, c.Ativa, c.Produtos.Count))
            .ToListAsync(ct);

    public async Task<Resultado> SalvarAsync(int id, string nome, bool ativa, CancellationToken ct = default)
    {
        var limpo = Texto.Limpar(nome);
        if (string.IsNullOrEmpty(limpo) || limpo.Length > 100) return Resultado.Falha("Informe um nome de até 100 caracteres.");
        if (await _db.Categorias.AnyAsync(c => c.Nome == limpo && c.Id != id, ct)) return Resultado.Falha("Já existe uma categoria com este nome.");

        var agora = _relogio.AgoraUtc;
        Categoria categoria;
        if (id == 0)
        {
            categoria = new Categoria { CriadoEm = agora };
            _db.Categorias.Add(categoria);
        }
        else
        {
            var existente = await _db.Categorias.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (existente is null) return Resultado.Falha("Categoria não encontrada.");
            categoria = existente;
            categoria.AlteradoEm = agora;
        }

        categoria.Nome = limpo;
        categoria.Ativa = ativa;
        var baseSlug = Texto.GerarSlug(limpo);
        var slug = baseSlug;
        for (var i = 2; await _db.Categorias.AnyAsync(c => c.Slug == slug && c.Id != id, ct); i++) slug = $"{baseSlug}-{i}";
        categoria.Slug = slug;

        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }
}
