using Microsoft.AspNetCore.Mvc;
using PortalVendas.Application.Admin;
using PortalVendas.Web.Areas.Admin.Models;
using PortalVendas.Web.Infra;

namespace PortalVendas.Web.Areas.Admin.Controllers;

public class ProdutosController : AdminControllerBase
{
    private readonly ProdutoAdminService _produtos;

    public ProdutosController(ProdutoAdminService produtos) => _produtos = produtos;

    public async Task<IActionResult> Index(string? termo, int? categoriaId, int pagina = 1, CancellationToken ct = default) =>
        View(new ProdutosIndexViewModel
        {
            Produtos = await _produtos.ListarAsync(termo, categoriaId, pagina, ct),
            Categorias = await _produtos.ListarCategoriasAsync(ct),
            Termo = termo,
            CategoriaId = categoriaId
        });

    public async Task<IActionResult> Novo(CancellationToken ct) =>
        View("Editar", new ProdutoEdicaoViewModel { Categorias = await _produtos.ListarCategoriasAsync(ct) });

    public async Task<IActionResult> Editar(int id, CancellationToken ct)
    {
        var produto = await _produtos.ObterAsync(id, ct);
        if (produto is null) return NotFound();
        var modelo = ProdutoEdicaoViewModel.De(produto);
        modelo.Categorias = await _produtos.ListarCategoriasAsync(ct);
        return View(modelo);
    }

    /// <summary>RF17</summary>
    [HttpPost]
    public async Task<IActionResult> Salvar(ProdutoEdicaoViewModel modelo, CancellationToken ct)
    {
        decimal LerObrigatorio(string? texto, string campo)
        {
            if (Formatacao.TentarLerDecimal(texto, out var v)) return v;
            ModelState.AddModelError(campo, "Valor inválido. Use o formato 1.234,56.");
            return 0;
        }

        var preco = LerObrigatorio(modelo.Preco, nameof(modelo.Preco));
        decimal? precoDe = null;
        if (!string.IsNullOrWhiteSpace(modelo.PrecoDe)) precoDe = LerObrigatorio(modelo.PrecoDe, nameof(modelo.PrecoDe));
        var peso = LerObrigatorio(modelo.PesoKg, nameof(modelo.PesoKg));
        var altura = LerObrigatorio(modelo.AlturaCm, nameof(modelo.AlturaCm));
        var largura = LerObrigatorio(modelo.LarguraCm, nameof(modelo.LarguraCm));
        var comprimento = LerObrigatorio(modelo.ComprimentoCm, nameof(modelo.ComprimentoCm));

        if (ModelState.IsValid)
        {
            var resultado = await _produtos.SalvarAsync(new DadosProduto(
                modelo.Id, modelo.CategoriaId, modelo.Sku, modelo.Nome, modelo.Descricao, preco, precoDe, modelo.Estoque,
                peso, altura, largura, comprimento, modelo.Ativo), Autor, ct);

            if (resultado.Sucesso)
            {
                Sucesso(modelo.Id == 0 ? "Produto criado. Agora adicione as fotos." : "Produto salvo.");
                return RedirectToAction(nameof(Editar), new { id = resultado.Valor });
            }
            ModelState.AddModelError(string.Empty, resultado.Erro!);
        }

        modelo.Categorias = await _produtos.ListarCategoriasAsync(ct);
        if (modelo.Id > 0)
        {
            var atual = await _produtos.ObterAsync(modelo.Id, ct);
            if (atual is not null)
            {
                modelo.Imagens = atual.Imagens.OrderBy(i => i.Ordem).ToList();
                modelo.EstoqueReservado = atual.EstoqueReservado;
                modelo.Slug = atual.Slug;
            }
        }
        return View("Editar", modelo);
    }

    [HttpPost]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> AdicionarImagem(int id, IFormFile? arquivo, CancellationToken ct)
    {
        if (arquivo is null || arquivo.Length == 0)
        {
            Erro("Escolha um arquivo.");
            return RedirectToAction(nameof(Editar), new { id });
        }
        if (arquivo.Length > ValidadorImagem.TamanhoMaximoBytes)
        {
            Erro("A imagem deve ter no máximo 2 MB.");
            return RedirectToAction(nameof(Editar), new { id });
        }

        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, ct);
        var resultado = await _produtos.AdicionarImagemAsync(id, memoria.ToArray(), arquivo.FileName, ct);
        if (resultado.Sucesso) Sucesso("Imagem adicionada."); else Erro(resultado.Erro);
        return RedirectToAction(nameof(Editar), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RemoverImagem(int id, int imagemId, CancellationToken ct)
    {
        var r = await _produtos.RemoverImagemAsync(id, imagemId, ct);
        if (r.Sucesso) Sucesso("Imagem removida."); else Erro(r.Erro);
        return RedirectToAction(nameof(Editar), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ImagemPrincipal(int id, int imagemId, CancellationToken ct)
    {
        var r = await _produtos.DefinirImagemPrincipalAsync(id, imagemId, ct);
        if (r.Sucesso) Sucesso("Foto principal alterada."); else Erro(r.Erro);
        return RedirectToAction(nameof(Editar), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Excluir(int id, CancellationToken ct)
    {
        var r = await _produtos.ExcluirAsync(id, Autor, ct);
        if (!r.Sucesso)
        {
            Erro(r.Erro);
            return RedirectToAction(nameof(Editar), new { id });
        }
        Sucesso(r.Valor ? "Produto excluído." : "O produto já teve vendas, então foi desativado (sai da loja, mas o histórico fica).");
        return RedirectToAction(nameof(Index));
    }
}
