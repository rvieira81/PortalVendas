using System.Text;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Contas;
using PortalVendas.Application.Pagamentos;

namespace PortalVendas.Tests.Infraestrutura;

public class TotpTests
{
    // Vetores oficiais da RFC 6238 (apêndice B), SHA-1, 8 dígitos
    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    public void Calcula_conforme_rfc6238(long unix, string esperado)
    {
        var chave = Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal(esperado, Totp.Calcular(chave, unix / 30, digitos: 8));
    }

    [Fact]
    public void Base32_ida_e_volta()
    {
        var segredo = Totp.GerarSegredo();
        Assert.Equal(32, segredo.Length);
        var bytes = Totp.Base32Decodificar(segredo);
        Assert.Equal(20, bytes.Length);
        Assert.Equal(segredo, Totp.Base32Codificar(bytes));
    }

    [Fact]
    public void Aceita_janela_anterior_e_recusa_codigo_antigo()
    {
        var segredo = Totp.GerarSegredo();
        var agora = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var chave = Totp.Base32Decodificar(segredo);
        var contador = Totp.Contador(agora);

        Assert.True(Totp.Validar(segredo, Totp.Calcular(chave, contador), agora));
        Assert.True(Totp.Validar(segredo, Totp.Calcular(chave, contador - 1), agora));
        Assert.False(Totp.Validar(segredo, Totp.Calcular(chave, contador - 3), agora));
        Assert.False(Totp.Validar(segredo, "12345", agora));
        Assert.False(Totp.Validar(segredo, "abcdef", agora));
    }
}

public class AssinaturaWebhookTests
{
    private const string Segredo = "segredo-do-webhook";

    [Fact]
    public void Aceita_assinatura_correta()   // RF13
    {
        var manifesto = AssinaturaWebhook.MontarManifesto("123456789", "req-1", "1704908010");
        Assert.Equal("id:123456789;request-id:req-1;ts:1704908010;", manifesto);
        var v1 = AssinaturaWebhook.Calcular(manifesto, Segredo);

        Assert.True(AssinaturaWebhook.Validar($"ts=1704908010,v1={v1}", "req-1", "123456789", Segredo));
    }

    [Fact]
    public void Recusa_assinatura_adulterada_ou_ausente()
    {
        var v1 = AssinaturaWebhook.Calcular(AssinaturaWebhook.MontarManifesto("123", "req-1", "1"), Segredo);

        Assert.False(AssinaturaWebhook.Validar($"ts=1,v1={v1}", "req-1", "999", Segredo));        // outro id
        Assert.False(AssinaturaWebhook.Validar($"ts=2,v1={v1}", "req-1", "123", Segredo));        // outro ts
        Assert.False(AssinaturaWebhook.Validar($"ts=1,v1={v1}", "req-1", "123", "outro"));        // outra chave
        Assert.False(AssinaturaWebhook.Validar(null, "req-1", "123", Segredo));
        Assert.False(AssinaturaWebhook.Validar("lixo", "req-1", "123", Segredo));
        Assert.False(AssinaturaWebhook.Validar($"ts=1,v1={v1}", "req-1", "123", ""));             // chave não configurada
    }

    [Fact]
    public void Id_alfanumerico_vai_em_minusculas() =>
        Assert.StartsWith("id:abc123;", AssinaturaWebhook.MontarManifesto("ABC123", "r", "1"));
}

public class ValidadorImagemTests
{
    private static byte[] Conteudo(params byte[] inicio)
    {
        var b = new byte[64];
        inicio.CopyTo(b, 0);
        return b;
    }

    [Fact]
    public void Aceita_jpg_png_e_webp_pela_assinatura()   // RF17
    {
        Assert.Null(ValidadorImagem.Validar(Conteudo(0xFF, 0xD8, 0xFF, 0xE0), "foto.jpeg", out var ext1));
        Assert.Equal(".jpg", ext1);
        Assert.Null(ValidadorImagem.Validar(Conteudo(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), "foto.png", out var ext2));
        Assert.Equal(".png", ext2);
        var webp = Conteudo((byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P');
        Assert.Null(ValidadorImagem.Validar(webp, "foto.webp", out var ext3));
        Assert.Equal(".webp", ext3);
    }

    [Fact]
    public void Recusa_executavel_disfarcado_de_jpg() =>
        Assert.NotNull(ValidadorImagem.Validar(Conteudo((byte)'M', (byte)'Z'), "foto.jpg", out _));

    [Fact]
    public void Recusa_extensao_nao_permitida() =>
        Assert.NotNull(ValidadorImagem.Validar(Conteudo(0xFF, 0xD8, 0xFF), "foto.svg", out _));

    [Fact]
    public void Recusa_acima_de_2mb() =>
        Assert.NotNull(ValidadorImagem.Validar(new byte[ValidadorImagem.TamanhoMaximoBytes + 1], "foto.jpg", out _));
}
