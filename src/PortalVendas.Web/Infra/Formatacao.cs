using System.Globalization;
using PortalVendas.Application.Comum;

namespace PortalVendas.Web.Infra;

public static class Formatacao
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Moeda(decimal valor) => valor.ToString("C", PtBr);

    public static string Numero(decimal valor, int casas = 2) => valor.ToString("N" + casas, PtBr);

    public static string DataHora(DateTime utc) => Horario.ParaBrasilia(utc).ToString("dd/MM/yyyy HH:mm", PtBr);

    public static string DataHora(DateTime? utc) => utc is null ? "—" : DataHora(utc.Value);

    public static string Data(DateTime utc) => Horario.ParaBrasilia(utc).ToString("dd/MM/yyyy", PtBr);

    public static string Prazo(int dias) => dias == 1 ? "1 dia útil" : $"{dias} dias úteis";

    public static string UrlImagem(string? arquivo) =>
        string.IsNullOrEmpty(arquivo) ? "/img/sem-imagem.svg" : $"/imagens/{arquivo}";

    /// <summary>Lê decimal digitado em pt-BR ("1.234,56") ou com ponto ("1234.56").</summary>
    public static bool TentarLerDecimal(string? texto, out decimal valor)
    {
        valor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var t = texto.Trim().Replace("R$", "").Replace(" ", "");
        if (t.Contains(',')) return decimal.TryParse(t, NumberStyles.Number, PtBr, out valor);
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
    }

    /// <summary>"ou 10x de R$ 25,90 sem juros" respeitando parcela mínima e máximo de parcelas da loja.</summary>
    public static string Parcelamento(decimal preco, PortalVendas.Application.Admin.ConfiguracaoLoja? configuracao)
    {
        var parcelas = configuracao?.ParcelasPermitidas(preco) ?? Math.Clamp((int)Math.Floor(preco / 10m), 1, 12);
        return parcelas > 1 ? $"ou {parcelas}x de {Moeda(Math.Round(preco / parcelas, 2))} sem juros" : "à vista no Pix, cartão ou boleto";
    }
}
