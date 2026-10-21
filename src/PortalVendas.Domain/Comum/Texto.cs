using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PortalVendas.Domain.Comum;

public static partial class Texto
{
    public static string SomenteDigitos(string? valor) =>
        string.IsNullOrEmpty(valor) ? string.Empty : new string(valor.Where(char.IsAsciiDigit).ToArray());

    /// <summary>"Café com Leite!" → "cafe-com-leite"</summary>
    public static string GerarSlug(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var normalizado = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);
        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var slug = CaracteresInvalidos().Replace(sb.ToString(), "-");
        slug = HifensRepetidos().Replace(slug, "-").Trim('-');
        return slug.Length > 200 ? slug[..200].TrimEnd('-') : slug;
    }

    /// <summary>Remove espaços extras; devolve null para texto vazio.</summary>
    public static string? Limpar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        return EspacosRepetidos().Replace(valor.Trim(), " ");
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex CaracteresInvalidos();

    [GeneratedRegex("-{2,}")]
    private static partial Regex HifensRepetidos();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex EspacosRepetidos();
}
