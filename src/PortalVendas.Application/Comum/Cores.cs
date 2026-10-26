using System.Globalization;
using System.Text.RegularExpressions;

namespace PortalVendas.Application.Comum;

/// <summary>Cores da marca: validação, contraste (WCAG) e variações mais claras/escuras.</summary>
public static partial class Cores
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();

    public static bool EhHex(string? cor) => cor is not null && Hex().IsMatch(cor);

    public static string Normalizar(string cor) => cor.Trim().ToUpperInvariant();

    private static (double R, double G, double B) Rgb(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255.0,
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255.0,
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255.0);

    private static double Luminancia(string hex)
    {
        static double Canal(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        var (r, g, b) = Rgb(hex);
        return 0.2126 * Canal(r) + 0.7152 * Canal(g) + 0.0722 * Canal(b);
    }

    /// <summary>Razão de contraste WCAG entre duas cores (1 a 21).</summary>
    public static double Contraste(string a, string b)
    {
        var (l1, l2) = (Luminancia(a), Luminancia(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Mistura com preto (fator &gt; 0) ou branco (fator &lt; 0). Ex.: Ajustar("#F5B82E", 0.1) escurece 10%.</summary>
    public static string Ajustar(string hex, double fator)
    {
        var (r, g, b) = Rgb(hex);
        double Misturar(double c) => fator >= 0 ? c * (1 - fator) : c + (1 - c) * -fator;
        static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return $"#{Byte(Misturar(r)):X2}{Byte(Misturar(g)):X2}{Byte(Misturar(b)):X2}";
    }

    /// <summary>"#12343B" → "18, 52, 59" (para variáveis rgb() do Bootstrap).</summary>
    public static string ParaRgbCss(string hex)
    {
        var (r, g, b) = Rgb(hex);
        return string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(r * 255)}, {(int)Math.Round(g * 255)}, {(int)Math.Round(b * 255)}");
    }
}
