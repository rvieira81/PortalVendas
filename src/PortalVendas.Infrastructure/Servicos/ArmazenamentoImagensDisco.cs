using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;

namespace PortalVendas.Infrastructure.Servicos;

public class ImagensOptions
{
    public const string Secao = "Imagens";
    /// <summary>Pasta física FORA da pasta publicada do site (senão o deploy apaga). Ex.: D:\PortalVendas\imagens</summary>
    public string Pasta { get; set; } = "imagens";
    /// <summary>Caminho público em que a pasta é servida.</summary>
    public string CaminhoPublico { get; set; } = "/imagens";
}

public partial class ArmazenamentoImagensDisco : IArmazenamentoImagens
{
    private readonly string _pasta;
    private readonly ILogger<ArmazenamentoImagensDisco> _logger;

    public ArmazenamentoImagensDisco(string pastaAbsoluta, ILogger<ArmazenamentoImagensDisco> logger)
    {
        _pasta = pastaAbsoluta;
        _logger = logger;
        Directory.CreateDirectory(_pasta);
    }

    public async Task<string> SalvarAsync(byte[] conteudo, string extensao, CancellationToken ct = default)
    {
        if (extensao is not (".jpg" or ".png" or ".webp")) throw new ArgumentException("Extensão não permitida.", nameof(extensao));
        var nome = $"{Guid.NewGuid():N}{extensao}";
        await File.WriteAllBytesAsync(Path.Combine(_pasta, nome), conteudo, ct);
        return nome;
    }

    public Task RemoverAsync(string arquivo, CancellationToken ct = default)
    {
        // Só nomes gerados por nós: impede apagar arquivos fora da pasta (path traversal)
        if (!NomeValido().IsMatch(arquivo)) return Task.CompletedTask;
        try
        {
            File.Delete(Path.Combine(_pasta, arquivo));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Não foi possível apagar a imagem {Arquivo}", arquivo);
        }
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"^[a-f0-9]{32}\.(jpg|png|webp)$")]
    private static partial Regex NomeValido();
}
