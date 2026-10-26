using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Tests.Apoio;

namespace PortalVendas.Tests.Aplicacao;

public class IdentidadeLojaServiceTests
{
    private static IdentidadeEditavel Editavel(string cor1 = "#12343B", string cor2 = "#F5B82E", string? email = null,
        string? instagram = null, string? link = null) =>
        new(cor1, cor2, "(19) 99999-0000", null, email, "Rua Exemplo, 100", "Seg. a sex., 9h às 18h",
            instagram, null, null, "Frete grátis acima de R$ 199", null, "Semana Prado", null, null, link);

    [Fact]
    public async Task Sem_licenca_usa_nome_e_cnpj_de_demonstracao()
    {
        using var c = new Cenario();
        var servico = new IdentidadeLojaService(c.Db, new LicencaFalsa(), new ImagensFalsas(), c.Relogio);

        var i = await servico.ObterAsync();

        Assert.True(i.Demonstracao);
        Assert.Equal(IdentidadeLojaService.NomeDemonstracao, i.NomeLoja);
        Assert.Equal(IdentidadeLojaService.CorPrincipalPadrao, i.CorPrincipal);
    }

    [Fact]
    public async Task Nome_razao_e_cnpj_vem_da_licenca_nao_do_banco()
    {
        using var c = new Cenario();
        // Mesmo que alguém grave estas chaves direto no banco, a loja continua usando a licença
        c.Db.Configuracoes.Add(new PortalVendas.Domain.Frete.Configuracao { Chave = "Loja.Nome", Valor = "Loja Pirata", AlteradoEm = c.Relogio.AgoraUtc, AlteradoPor = "x" });
        await c.Db.SaveChangesAsync();
        var licenca = new LicencaFalsa
        {
            Estado = new EstadoLicenca(SituacaoLicenca.Valida, "ok", new DadosLicenca
            {
                NomeLoja = "Prado Shop", RazaoSocial = "Prado Shop Comércio Ltda.", Cnpj = "12345678000195", Dominios = new[] { "pradoshop.com.br" }
            })
        };

        var i = await new IdentidadeLojaService(c.Db, licenca, new ImagensFalsas(), c.Relogio).ObterAsync();

        Assert.False(i.Demonstracao);
        Assert.Equal("Prado Shop", i.NomeLoja);
        Assert.Equal("12.345.678/0001-95", i.CnpjFormatado);
    }

    [Fact]
    public async Task Salva_cores_e_contatos_e_monta_link_do_whatsapp()
    {
        using var c = new Cenario();
        var servico = new IdentidadeLojaService(c.Db, new LicencaFalsa(), new ImagensFalsas(), c.Relogio);

        var r = await servico.SalvarAsync(Editavel("#1f2a44", "#ff8a3d", "contato@pradoshop.com.br", "https://instagram.com/pradoshop", "/categoria/geral"), "admin");

        Assert.True(r.Sucesso, r.Erro);
        var i = await new IdentidadeLojaService(c.NovoContexto(), new LicencaFalsa(), new ImagensFalsas(), c.Relogio).ObterAsync();
        Assert.Equal("#1F2A44", i.CorPrincipal);
        Assert.Equal("#FF8A3D", i.CorDestaque);
        Assert.Equal("https://wa.me/5519999990000", i.WhatsAppLink);
        Assert.Equal("Semana Prado", i.BannerTitulo);
        Assert.Equal("/categoria/geral", i.BannerLink);
    }

    [Theory]
    [InlineData("#F5F5F5", "#12343B")]   // principal clara demais para texto branco
    [InlineData("#12343B", "#1F2A44")]   // destaque sem contraste com a principal
    [InlineData("vermelho", "#F5B82E")]  // fora do formato
    public async Task Recusa_cores_sem_contraste_ou_invalidas(string cor1, string cor2)
    {
        using var c = new Cenario();
        var r = await new IdentidadeLojaService(c.Db, new LicencaFalsa(), new ImagensFalsas(), c.Relogio).SalvarAsync(Editavel(cor1, cor2), "admin");
        Assert.False(r.Sucesso);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//site-malicioso.com")]
    [InlineData("http://sem-https.com")]
    public async Task Recusa_links_inseguros_no_banner(string link)
    {
        using var c = new Cenario();
        var r = await new IdentidadeLojaService(c.Db, new LicencaFalsa(), new ImagensFalsas(), c.Relogio).SalvarAsync(Editavel(link: link), "admin");
        Assert.False(r.Sucesso);
    }

    [Fact]
    public async Task Recusa_rede_social_sem_https()
    {
        using var c = new Cenario();
        var r = await new IdentidadeLojaService(c.Db, new LicencaFalsa(), new ImagensFalsas(), c.Relogio)
            .SalvarAsync(Editavel(instagram: "javascript:alert(1)"), "admin");
        Assert.False(r.Sucesso);
    }

    [Theory]
    [InlineData("#FFFFFF", "#000000", 21.0)]
    [InlineData("#12343B", "#FFFFFF", 13.3)]
    public void Contraste_segue_a_formula_wcag(string a, string b, double esperado) =>
        Assert.Equal(esperado, Cores.Contraste(a, b), 1);

    [Fact]
    public void Ajustar_escurece_e_clareia()
    {
        Assert.Equal("#000000", Cores.Ajustar("#12343B", 1));
        Assert.Equal("#FFFFFF", Cores.Ajustar("#12343B", -1));
        Assert.Equal("18, 52, 59", Cores.ParaRgbCss("#12343B"));
    }
}
