using System.Security.Cryptography;
using System.Text;

namespace PortalVendas.Application.Contas;

/// <summary>Senha de uso único baseada em tempo (RFC 6238): 6 dígitos, 30 segundos, HMAC-SHA1.</summary>
public static class Totp
{
    public const int Digitos = 6;
    public const int PeriodoSegundos = 30;
    private const string AlfabetoBase32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GerarSegredo() => Base32Codificar(RandomNumberGenerator.GetBytes(20));

    public static long Contador(DateTime agoraUtc) =>
        new DateTimeOffset(DateTime.SpecifyKind(agoraUtc, DateTimeKind.Utc)).ToUnixTimeSeconds() / PeriodoSegundos;

    public static string Calcular(byte[] chave, long contador, int digitos = Digitos)
    {
        Span<byte> mensagem = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            mensagem[i] = (byte)(contador & 0xFF);
            contador >>= 8;
        }

        var hash = HMACSHA1.HashData(chave, mensagem);
        var deslocamento = hash[^1] & 0x0F;
        var binario = ((hash[deslocamento] & 0x7F) << 24)
                      | (hash[deslocamento + 1] << 16)
                      | (hash[deslocamento + 2] << 8)
                      | hash[deslocamento + 3];

        var modulo = (int)Math.Pow(10, digitos);
        return (binario % modulo).ToString(new string('0', digitos));
    }

    /// <summary>Aceita o código da janela atual e de uma janela antes/depois (relógio do celular adiantado ou atrasado).</summary>
    public static bool Validar(string segredoBase32, string? codigo, DateTime agoraUtc, int janelas = 1)
    {
        var limpo = codigo?.Replace(" ", string.Empty).Trim();
        if (limpo is null || limpo.Length != Digitos || !limpo.All(char.IsAsciiDigit)) return false;

        byte[] chave;
        try { chave = Base32Decodificar(segredoBase32); }
        catch (FormatException) { return false; }

        var atual = Contador(agoraUtc);
        var esperadoBytes = Encoding.ASCII.GetBytes(limpo);
        var valido = false;
        for (var d = -janelas; d <= janelas; d++)
        {
            var calculado = Encoding.ASCII.GetBytes(Calcular(chave, atual + d));
            // Comparação em tempo constante; não para no primeiro acerto
            valido |= CryptographicOperations.FixedTimeEquals(calculado, esperadoBytes);
        }
        return valido;
    }

    public static string MontarUri(string emissor, string conta, string segredoBase32) =>
        $"otpauth://totp/{Uri.EscapeDataString(emissor)}:{Uri.EscapeDataString(conta)}" +
        $"?secret={segredoBase32}&issuer={Uri.EscapeDataString(emissor)}&algorithm=SHA1&digits={Digitos}&period={PeriodoSegundos}";

    /// <summary>Exibe o segredo em grupos de 4 para digitação manual.</summary>
    public static string FormatarParaLeitura(string segredoBase32) =>
        string.Join(' ', Enumerable.Range(0, (segredoBase32.Length + 3) / 4)
            .Select(i => segredoBase32.Substring(i * 4, Math.Min(4, segredoBase32.Length - i * 4))));

    public static string Base32Codificar(byte[] dados)
    {
        var sb = new StringBuilder((dados.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in dados)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(AlfabetoBase32[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(AlfabetoBase32[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Base32Decodificar(string texto)
    {
        var limpo = texto.Trim().TrimEnd('=').Replace(" ", string.Empty).ToUpperInvariant();
        var saida = new List<byte>(limpo.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in limpo)
        {
            var valor = AlfabetoBase32.IndexOf(c);
            if (valor < 0) throw new FormatException("Caractere inválido em Base32.");
            buffer = (buffer << 5) | valor;
            bits += 5;
            if (bits >= 8)
            {
                saida.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return saida.ToArray();
    }
}
