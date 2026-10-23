using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Domain.Usuarios;

namespace PortalVendas.Infrastructure.Servicos;

/// <summary>PBKDF2 do ASP.NET Core (ADR-004).</summary>
public class HashSenha : IHashSenha
{
    private static readonly Usuario Nenhum = new();
    private readonly PasswordHasher<Usuario> _hasher = new();

    public string Gerar(string senha) => _hasher.HashPassword(Nenhum, senha);

    public VerificacaoSenha Verificar(string hash, string senha)
    {
        if (string.IsNullOrEmpty(hash) || hash.StartsWith('!')) return VerificacaoSenha.Falha;   // "!" = conta anonimizada ou criada no WhatsApp (sem senha)
        try
        {
            return _hasher.VerifyHashedPassword(Nenhum, hash, senha) switch
            {
                PasswordVerificationResult.Success => VerificacaoSenha.Sucesso,
                PasswordVerificationResult.SuccessRehashNeeded => VerificacaoSenha.SucessoPrecisaRehash,
                _ => VerificacaoSenha.Falha
            };
        }
        catch (FormatException)
        {
            return VerificacaoSenha.Falha;
        }
    }
}

/// <summary>Cifra o segredo TOTP com o Data Protection (chaves protegidas por DPAPI no servidor).</summary>
public class ProtetorDados : IProtetorDados
{
    private readonly IDataProtector _protetor;

    public ProtetorDados(IDataProtectionProvider provedor) => _protetor = provedor.CreateProtector("PortalVendas.Totp.v1");

    public string Proteger(string texto) => _protetor.Protect(texto);

    public string? Desproteger(string textoProtegido)
    {
        try { return _protetor.Unprotect(textoProtegido); }
        catch (CryptographicException) { return null; }
    }
}
