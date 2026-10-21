namespace PortalVendas.Application.Comum;

public record Resultado(bool Sucesso, string? Erro)
{
    public static Resultado Ok() => new(true, null);
    public static Resultado Falha(string erro) => new(false, erro);
}

public record Resultado<T>(bool Sucesso, string? Erro, T? Valor)
{
    public static Resultado<T> Ok(T valor) => new(true, null, valor);
    public static Resultado<T> Falha(string erro) => new(false, erro, default);
}

public record Pagina<T>(IReadOnlyList<T> Itens, int PaginaAtual, int TamanhoPagina, int TotalItens)
{
    public int TotalPaginas => TamanhoPagina <= 0 ? 0 : (int)Math.Ceiling(TotalItens / (double)TamanhoPagina);
    public bool TemAnterior => PaginaAtual > 1;
    public bool TemProxima => PaginaAtual < TotalPaginas;

    public static int Normalizar(int pagina) => pagina < 1 ? 1 : pagina;
}

public static class Autores
{
    public const string Sistema = "sistema";
    public const string Gateway = "mercadopago";
    public static string Cliente(int usuarioId) => $"cliente:{usuarioId}";
}
