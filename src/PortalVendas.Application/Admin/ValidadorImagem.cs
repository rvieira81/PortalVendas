namespace PortalVendas.Application.Admin;

/// <summary>RF17: aceita só JPG, PNG e WebP de até 2 MB, conferindo a assinatura do arquivo (não só a extensão).</summary>
public static class ValidadorImagem
{
    public const long TamanhoMaximoBytes = 2 * 1024 * 1024;

    /// <summary>Devolve a extensão segura (".jpg", ".png", ".webp") ou null se o conteúdo não for uma imagem aceita.</summary>
    public static string? IdentificarExtensao(ReadOnlySpan<byte> conteudo)
    {
        if (conteudo.Length >= 3 && conteudo[0] == 0xFF && conteudo[1] == 0xD8 && conteudo[2] == 0xFF)
            return ".jpg";
        if (conteudo.Length >= 8 && conteudo[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ".png";
        if (conteudo.Length >= 12 && conteudo[..4].SequenceEqual("RIFF"u8) && conteudo[8..12].SequenceEqual("WEBP"u8))
            return ".webp";
        return null;
    }

    public static string? Validar(byte[] conteudo, string nomeOriginal, out string? extensao)
    {
        extensao = null;
        if (conteudo.Length == 0) return "Arquivo vazio.";
        if (conteudo.Length > TamanhoMaximoBytes) return "A imagem deve ter no máximo 2 MB.";

        var extensaoDeclarada = Path.GetExtension(nomeOriginal ?? string.Empty).ToLowerInvariant();
        if (extensaoDeclarada is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            return "Formato não aceito. Envie JPG, PNG ou WebP.";

        extensao = IdentificarExtensao(conteudo);
        if (extensao is null) return "O arquivo não é uma imagem JPG, PNG ou WebP válida.";
        return null;
    }
}
