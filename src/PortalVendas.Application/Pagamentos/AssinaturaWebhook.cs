using System.Security.Cryptography;
using System.Text;

namespace PortalVendas.Application.Pagamentos;

/// <summary>
/// Validação do cabeçalho x-signature do webhook do Mercado Pago:
/// HMAC-SHA256 de "id:{data.id};request-id:{x-request-id};ts:{ts};" com a chave secreta do webhook.
/// </summary>
public static class AssinaturaWebhook
{
    public static bool Validar(string? xSignature, string? xRequestId, string? dataId, string? segredo)
    {
        if (string.IsNullOrWhiteSpace(xSignature) || string.IsNullOrWhiteSpace(segredo) || string.IsNullOrWhiteSpace(dataId))
            return false;

        string? ts = null, v1 = null;
        foreach (var parte in xSignature.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = parte.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) continue;
            if (kv[0].Equals("ts", StringComparison.OrdinalIgnoreCase)) ts = kv[1];
            else if (kv[0].Equals("v1", StringComparison.OrdinalIgnoreCase)) v1 = kv[1];
        }
        if (ts is null || v1 is null) return false;

        var manifesto = MontarManifesto(dataId, xRequestId, ts);
        var esperado = Calcular(manifesto, segredo);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(esperado),
            Encoding.ASCII.GetBytes(v1.ToLowerInvariant()));
    }

    public static string MontarManifesto(string dataId, string? xRequestId, string ts)
    {
        // Ids alfanuméricos devem ir em minúsculas; numéricos não mudam
        var id = dataId.ToLowerInvariant();
        var sb = new StringBuilder();
        sb.Append("id:").Append(id).Append(';');
        if (!string.IsNullOrEmpty(xRequestId)) sb.Append("request-id:").Append(xRequestId).Append(';');
        sb.Append("ts:").Append(ts).Append(';');
        return sb.ToString();
    }

    public static string Calcular(string manifesto, string segredo)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(manifesto));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
