using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PortalVendas.Application.WhatsApp;

namespace PortalVendas.Infrastructure.WhatsApp;

/// <summary>Traduz os webhooks da Meta e da Evolution para <see cref="MensagemRecebida"/>.</summary>
public static class LeitorWebhook
{
    // ================================================================== Meta (Cloud API)

    /// <summary>Confere o cabeçalho X-Hub-Signature-256 (HMAC-SHA256 do corpo com o App Secret).</summary>
    public static bool AssinaturaMetaValida(byte[] corpo, string? cabecalho, string? appSecret)
    {
        if (string.IsNullOrEmpty(appSecret) || string.IsNullOrEmpty(cabecalho) || !cabecalho.StartsWith("sha256=", StringComparison.Ordinal))
            return false;
        byte[] recebida;
        try { recebida = Convert.FromHexString(cabecalho[7..]); }
        catch (FormatException) { return false; }
        var esperada = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), corpo);
        return CryptographicOperations.FixedTimeEquals(esperada, recebida);
    }

    public static IReadOnlyList<MensagemRecebida> LerMeta(JsonElement raiz)
    {
        var lista = new List<MensagemRecebida>();
        if (!raiz.TryGetProperty("entry", out var entradas) || entradas.ValueKind != JsonValueKind.Array) return lista;

        foreach (var entrada in entradas.EnumerateArray())
        {
            if (!entrada.TryGetProperty("changes", out var mudancas) || mudancas.ValueKind != JsonValueKind.Array) continue;
            foreach (var mudanca in mudancas.EnumerateArray())
            {
                if (!mudanca.TryGetProperty("value", out var valor)) continue;
                if (!valor.TryGetProperty("messages", out var mensagens) || mensagens.ValueKind != JsonValueKind.Array) continue;

                var nomes = new Dictionary<string, string>();
                if (valor.TryGetProperty("contacts", out var contatos) && contatos.ValueKind == JsonValueKind.Array)
                    foreach (var c in contatos.EnumerateArray())
                        if (Texto(c, "wa_id") is { } wa && c.TryGetProperty("profile", out var perfil) && Texto(perfil, "name") is { } nome)
                            nomes[wa] = nome;

                foreach (var m in mensagens.EnumerateArray())
                {
                    var de = Texto(m, "from");
                    var id = Texto(m, "id");
                    if (de is null || id is null) continue;
                    var quando = long.TryParse(Texto(m, "timestamp"), out var ts) ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime : DateTime.UtcNow;
                    var (texto, opcao) = ConteudoMeta(m);
                    lista.Add(new MensagemRecebida(de, nomes.GetValueOrDefault(de), texto, opcao, "meta:" + id, quando));
                }
            }
        }
        return lista;
    }

    private static (string Texto, string? Opcao) ConteudoMeta(JsonElement m)
    {
        switch (Texto(m, "type"))
        {
            case "text":
                return (m.TryGetProperty("text", out var t) ? Texto(t, "body") ?? "" : "", null);
            case "interactive":
                if (!m.TryGetProperty("interactive", out var i)) break;
                if (i.TryGetProperty("button_reply", out var b)) return (Texto(b, "title") ?? "", Texto(b, "id"));
                if (i.TryGetProperty("list_reply", out var l)) return (Texto(l, "title") ?? "", Texto(l, "id"));
                break;
            case "button":   // botão de modelo (template)
                if (m.TryGetProperty("button", out var bt)) return (Texto(bt, "text") ?? "", Texto(bt, "payload"));
                break;
            case "image":
                return ("[imagem] " + (m.TryGetProperty("image", out var img) ? Texto(img, "caption") : null), null);
            case "audio":
                return ("[áudio]", null);
            case "location":
                return ("[localização]", null);
        }
        return ($"[{Texto(m, "type") ?? "mensagem"}]", null);
    }

    // ================================================================== Evolution API v2

    public static IReadOnlyList<MensagemRecebida> LerEvolution(JsonElement raiz)
    {
        var lista = new List<MensagemRecebida>();
        var evento = (Texto(raiz, "event") ?? "").Replace('_', '.').ToLowerInvariant();
        if (evento != "messages.upsert" || !raiz.TryGetProperty("data", out var dados)) return lista;

        IEnumerable<JsonElement> itens = dados.ValueKind == JsonValueKind.Array ? dados.EnumerateArray() : new[] { dados };
        foreach (var d in itens)
        {
            if (!d.TryGetProperty("key", out var chave)) continue;
            if (chave.TryGetProperty("fromMe", out var deMim) && deMim.ValueKind == JsonValueKind.True) continue;   // eco do que a loja enviou
            var jid = Texto(chave, "remoteJid");
            if (jid is null || jid.EndsWith("@g.us", StringComparison.Ordinal) || jid.StartsWith("status@", StringComparison.Ordinal)) continue;
            if (jid.EndsWith("@lid", StringComparison.Ordinal))
                jid = Texto(chave, "senderPn") ?? Texto(chave, "remoteJidAlt") ?? Texto(d, "senderPn");   // contas com identificador oculto
            var telefone = jid is null ? null : new string(jid.Split('@')[0].Where(char.IsAsciiDigit).ToArray());
            var id = Texto(chave, "id");
            if (string.IsNullOrEmpty(telefone) || id is null) continue;

            var quando = d.TryGetProperty("messageTimestamp", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var s)
                ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime
                : DateTime.UtcNow;
            var (texto, opcao) = d.TryGetProperty("message", out var msg) ? ConteudoEvolution(msg) : ("[mensagem]", null);
            lista.Add(new MensagemRecebida(telefone, Texto(d, "pushName"), texto, opcao, "evo:" + id, quando));
        }
        return lista;
    }

    private static (string Texto, string? Opcao) ConteudoEvolution(JsonElement m)
    {
        if (Texto(m, "conversation") is { } conversa) return (conversa, null);
        if (m.TryGetProperty("extendedTextMessage", out var ext) && Texto(ext, "text") is { } texto) return (texto, null);
        if (m.TryGetProperty("buttonsResponseMessage", out var br))
            return (Texto(br, "selectedDisplayText") ?? "", Texto(br, "selectedButtonId"));
        if (m.TryGetProperty("listResponseMessage", out var lr))
            return (Texto(lr, "title") ?? "", lr.TryGetProperty("singleSelectReply", out var sr) ? Texto(sr, "selectedRowId") : null);
        if (m.TryGetProperty("imageMessage", out var img)) return ("[imagem] " + Texto(img, "caption"), null);
        if (m.TryGetProperty("audioMessage", out _)) return ("[áudio]", null);
        return ("[mensagem]", null);
    }

    private static string? Texto(JsonElement e, string propriedade) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
