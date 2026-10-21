using PortalVendas.Domain.Comum;

namespace PortalVendas.Tests.Dominio;

public class DocumentosTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("529.982.247-24", false)]   // dígito verificador errado (RF05)
    [InlineData("111.111.111-11", false)]   // sequência repetida
    [InlineData("1234567890", false)]       // 10 dígitos
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Cpf_valida_digitos_verificadores(string? cpf, bool esperado) =>
        Assert.Equal(esperado, Cpf.EhValido(cpf));

    [Fact]
    public void Cpf_mascarado_esconde_inicio_e_fim() =>
        Assert.Equal("***.982.247-**", Cpf.Mascarar("52998224725"));

    [Theory]
    [InlineData("13084-000", true)]
    [InlineData("13084000", true)]
    [InlineData("1308400", false)]
    [InlineData("00000000", false)]
    public void Cep_valida_formato(string cep, bool esperado) => Assert.Equal(esperado, Cep.EhValido(cep));

    [Theory]
    [InlineData("(19) 99999-0000", true)]
    [InlineData("1933334444", true)]
    [InlineData("999990000", false)]
    public void Telefone_exige_ddd(string tel, bool esperado) => Assert.Equal(esperado, Telefone.EhValido(tel));

    [Theory]
    [InlineData("Café com Leite!", "cafe-com-leite")]
    [InlineData("  Fone   Bluetooth 5.0  ", "fone-bluetooth-5-0")]
    [InlineData("Ação & Reação", "acao-reacao")]
    public void Slug_remove_acentos_e_simbolos(string texto, string esperado) => Assert.Equal(esperado, Texto.GerarSlug(texto));

    [Fact]
    public void Dias_uteis_pulam_fim_de_semana()
    {
        var sexta = new DateOnly(2026, 10, 23);
        Assert.Equal(new DateOnly(2026, 10, 28), DiasUteis.Somar(sexta, 3));   // seg, ter, qua
    }
}
