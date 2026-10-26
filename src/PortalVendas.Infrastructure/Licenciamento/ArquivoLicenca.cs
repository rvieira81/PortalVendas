using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Domain.Comum;

namespace PortalVendas.Infrastructure.Licenciamento;

/// <summary>
/// Formato do arquivo .lic: {"dados": "&lt;JSON da licença em Base64&gt;", "assinatura": "&lt;ECDSA P-256/SHA-256 em Base64&gt;"}.
/// A assinatura cobre exatamente os bytes de "dados", então não há ambiguidade de serialização.
/// </summary>
public static class ArquivoLicenca
{
    public const string Produto = "PortalVendas";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private sealed record Envelope(string Dados, string Assinatura);

    /// <summary>Usado pela ferramenta do fornecedor (e pelos testes) para emitir uma licença.</summary>
    public static byte[] Emitir(DadosLicenca dados, ECDsa chavePrivada)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dados, Json);
        var assinatura = chavePrivada.SignData(bytes, HashAlgorithmName.SHA256);
        var envelope = new Envelope(Convert.ToBase64String(bytes), Convert.ToBase64String(assinatura));
        return JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(Json) { WriteIndented = true });
    }

    /// <summary>Confere assinatura e conteúdo. Devolve os dados ou o motivo da recusa.</summary>
    public static (DadosLicenca? Dados, string? Erro) Ler(byte[] conteudo, string chavePublicaBase64)
    {
        if (string.IsNullOrWhiteSpace(chavePublicaBase64))
            return (null, "Esta versão do sistema ainda não tem a chave pública do fornecedor configurada.");
        if (conteudo.Length == 0 || conteudo.Length > 64 * 1024)
            return (null, "Arquivo de licença vazio ou grande demais.");

        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(conteudo, Json);
        }
        catch (JsonException)
        {
            return (null, "Arquivo de licença em formato inválido.");
        }
        if (envelope is null || string.IsNullOrEmpty(envelope.Dados) || string.IsNullOrEmpty(envelope.Assinatura))
            return (null, "Arquivo de licença incompleto.");

        byte[] dadosBytes, assinatura;
        try
        {
            dadosBytes = Convert.FromBase64String(envelope.Dados);
            assinatura = Convert.FromBase64String(envelope.Assinatura);
        }
        catch (FormatException)
        {
            return (null, "Arquivo de licença corrompido.");
        }

        using (var ecdsa = ECDsa.Create())
        {
            try
            {
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(chavePublicaBase64), out _);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                return (null, "Chave pública do fornecedor inválida nesta versão do sistema.");
            }
            if (!ecdsa.VerifyData(dadosBytes, assinatura, HashAlgorithmName.SHA256))
                return (null, "Assinatura da licença inválida: o arquivo foi alterado ou não foi emitido pelo fornecedor.");
        }

        DadosLicenca? dados;
        try
        {
            dados = JsonSerializer.Deserialize<DadosLicenca>(dadosBytes, Json);
        }
        catch (JsonException)
        {
            return (null, "Conteúdo da licença inválido.");
        }
        if (dados is null || dados.Produto != Produto) return (null, "Esta licença é de outro produto.");
        if (string.IsNullOrWhiteSpace(dados.NomeLoja) || string.IsNullOrWhiteSpace(dados.RazaoSocial))
            return (null, "Licença sem nome da loja ou razão social.");
        if (!Cnpj.EhValido(dados.Cnpj)) return (null, "CNPJ da licença inválido.");
        if (dados.Dominios.Count == 0) return (null, "Licença sem domínio autorizado.");
        return (dados, null);
    }

    public static string Descrever(DadosLicenca d) =>
        new StringBuilder()
            .Append(d.NomeLoja).Append(" — ").Append(d.RazaoSocial)
            .Append(" — CNPJ ").Append(Cnpj.Formatar(d.Cnpj))
            .Append(" — ").Append(string.Join(", ", d.Dominios))
            .Append(d.Modulos is { Count: > 0 } m ? " — módulos: " + string.Join(", ", m) : "")
            .ToString();
}
