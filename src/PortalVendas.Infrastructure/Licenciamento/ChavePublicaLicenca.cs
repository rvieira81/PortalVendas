namespace PortalVendas.Infrastructure.Licenciamento;

/// <summary>
/// Chave pública (ECDSA P-256, SubjectPublicKeyInfo em Base64) usada para conferir a assinatura das licenças.
///
/// Gere o par de chaves UMA vez com a ferramenta do fornecedor:
///     dotnet run --project tools/PortalVendas.Licenciador -- chaves --saida C:\Licencas\chave-privada.pem
/// e cole aqui a chave pública que ela mostrar. A chave PRIVADA fica só com o fornecedor (nunca no repositório).
///
/// Enquanto este valor estiver vazio, nenhuma licença é aceita e a loja roda em modo demonstração (só em localhost).
/// Fica no código (e não no appsettings) de propósito: quem recebe o sistema compilado não consegue trocá-la.
/// </summary>
internal static class ChavePublicaLicenca
{
    public const string Valor = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEvDNzPeYSHpBG5THAjU+ShMeQwGDYkxpLs4dVWmv0tsYJq+WsI+8B7lOi6i9W3s9DIkWcWSIZTRXaGJlyP3Vc+w==";
}
