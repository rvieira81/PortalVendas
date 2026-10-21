using PortalVendas.Application.Admin;
using PortalVendas.Application.Frete;
using PortalVendas.Tests.Apoio;

namespace PortalVendas.Tests.Aplicacao;

public class FreteServiceTests
{
    private static readonly ItemFrete[] Itens = { new(1, 0.5m, 10, 15, 20, 100m, 2) };

    [Fact]
    public async Task Cota_no_provedor_e_soma_dias_de_manuseio()   // RF08
    {
        using var c = new Cenario();
        var r = await c.FreteService.CotarAsync("13084-000", Itens);

        Assert.True(r.Sucesso);
        Assert.Equal(2, r.Valor!.Count);
        var pac = r.Valor[0];
        Assert.Equal("ME-1", pac.Codigo);
        Assert.Equal(25.40m, pac.Valor);
        Assert.Equal(5 + 1, pac.PrazoDias);   // prazo da transportadora + 1 dia de manuseio
        Assert.Equal(Cenario.CepOrigem, c.Frete.Ultima!.CepOrigem);
        Assert.Equal("13084000", c.Frete.Ultima.CepDestino);
    }

    [Fact]
    public async Task Frete_gratis_na_opcao_mais_barata()
    {
        using var c = new Cenario();
        await c.DefinirConfiguracaoAsync(ConfiguracaoLojaService.ChaveFreteGratis, "150.00");

        var r = await c.FreteService.CotarAsync("13084000", Itens);   // subtotal 200

        Assert.Equal(0m, r.Valor![0].Valor);
        Assert.Equal(25.40m, r.Valor[0].ValorOriginal);
        Assert.Equal(48.90m, r.Valor[1].Valor);
    }

    [Fact]
    public async Task Sem_frete_gratis_abaixo_do_minimo()
    {
        using var c = new Cenario();
        await c.DefinirConfiguracaoAsync(ConfiguracaoLojaService.ChaveFreteGratis, "500.00");
        var r = await c.FreteService.CotarAsync("13084000", Itens);
        Assert.Equal(25.40m, r.Valor![0].Valor);
    }

    [Fact]
    public async Task Usa_contingencia_quando_o_provedor_cai()
    {
        using var c = new Cenario();
        c.Frete.Falhar = true;
        await c.AdicionarFaixaAsync("10000000", "19999999", 24.90m, 5);
        await c.AdicionarFaixaAsync("13000000", "13999999", 19.90m, 3);   // mais específica

        var r = await c.FreteService.CotarAsync("13084000", Itens);

        Assert.True(r.Sucesso);
        var opcao = Assert.Single(r.Valor!);
        Assert.True(opcao.Contingencia);
        Assert.Equal(19.90m, opcao.Valor);
        Assert.Equal(3 + 1, opcao.PrazoDias);
    }

    [Fact]
    public async Task Cep_nao_atendido()
    {
        using var c = new Cenario();
        c.Frete.Falhar = true;
        var r = await c.FreteService.CotarAsync("69900000", Itens);
        Assert.False(r.Sucesso);
        Assert.Equal("Não entregamos neste CEP.", r.Erro);
    }

    [Fact]
    public async Task Cep_invalido_nem_consulta_o_provedor()
    {
        using var c = new Cenario();
        var r = await c.FreteService.CotarAsync("123", Itens);
        Assert.False(r.Sucesso);
        Assert.Equal(0, c.Frete.Chamadas);
    }
}
