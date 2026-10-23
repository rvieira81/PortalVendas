using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Domain.Comum;
using PortalVendas.Infrastructure.Licenciamento;
using PortalVendas.Tests.Apoio;

namespace PortalVendas.Tests.Infraestrutura;

public sealed class LicencaTests : IDisposable
{
    private readonly ECDsa _chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _publica;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pv-licenca-" + Guid.NewGuid().ToString("N"));
    private readonly RelogioFalso _relogio = new(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));

    public LicencaTests()
    {
        _publica = Convert.ToBase64String(_chave.ExportSubjectPublicKeyInfo());
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose()
    {
        _chave.Dispose();
        try { Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
    }

    private static DadosLicenca Dados(DateTime? expira = null, params string[] dominios) => new()
    {
        Id = Guid.NewGuid(),
        Produto = ArquivoLicenca.Produto,
        NomeLoja = "Prado Shop",
        RazaoSocial = "Prado Shop Comércio Ltda.",
        Cnpj = "12345678000195",
        Dominios = dominios.Length == 0 ? new[] { "pradoshop.com.br", "www.pradoshop.com.br" } : dominios,
        EmitidaEm = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        ExpiraEm = expira
    };

    private ServicoLicenca Servico(string? arquivo = null) =>
        new(arquivo ?? Path.Combine(_pasta, "licenca.lic"), _publica, _relogio, NullLogger<ServicoLicenca>.Instance);

    // ---------------------------------------------------------------- CNPJ

    [Theory]
    [InlineData("12.345.678/0001-95", true)]
    [InlineData("11222333000181", true)]
    [InlineData("12.ABC.345/01DE-35", true)]    // CNPJ alfanumérico (Receita, a partir de 2026)
    [InlineData("12.345.678/0001-96", false)]
    [InlineData("11.111.111/1111-11", false)]
    [InlineData("1234567800019", false)]
    [InlineData("", false)]
    public void Cnpj_valida_digitos_inclusive_alfanumerico(string cnpj, bool esperado) =>
        Assert.Equal(esperado, Cnpj.EhValido(cnpj));

    [Fact]
    public void Cnpj_formata_com_mascara() => Assert.Equal("12.345.678/0001-95", Cnpj.Formatar("12345678000195"));

    // ---------------------------------------------------------------- arquivo assinado

    [Fact]
    public void Licenca_assinada_pelo_fornecedor_e_aceita()
    {
        var (dados, erro) = ArquivoLicenca.Ler(ArquivoLicenca.Emitir(Dados(), _chave), _publica);
        Assert.Null(erro);
        Assert.Equal("Prado Shop", dados!.NomeLoja);
    }

    [Fact]
    public void Licenca_com_nome_ou_cnpj_alterado_e_recusada()
    {
        var bytes = ArquivoLicenca.Emitir(Dados(), _chave);
        var envelope = JsonDocument.Parse(bytes).RootElement;
        var dados = Encoding.UTF8.GetString(Convert.FromBase64String(envelope.GetProperty("dados").GetString()!));
        var adulterado = Convert.ToBase64String(Encoding.UTF8.GetBytes(dados.Replace("Prado Shop", "Outra Loja")));
        var novo = JsonSerializer.SerializeToUtf8Bytes(new { dados = adulterado, assinatura = envelope.GetProperty("assinatura").GetString() });

        var (lida, erro) = ArquivoLicenca.Ler(novo, _publica);
        Assert.Null(lida);
        Assert.Contains("Assinatura", erro);
    }

    [Fact]
    public void Licenca_emitida_com_outra_chave_e_recusada()
    {
        using var outra = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (lida, _) = ArquivoLicenca.Ler(ArquivoLicenca.Emitir(Dados(), outra), _publica);
        Assert.Null(lida);
    }

    [Fact]
    public void Sem_chave_publica_configurada_nenhuma_licenca_e_aceita()
    {
        var (lida, erro) = ArquivoLicenca.Ler(ArquivoLicenca.Emitir(Dados(), _chave), "");
        Assert.Null(lida);
        Assert.NotNull(erro);
    }

    [Fact]
    public void Arquivo_que_nao_e_licenca_e_recusado()
    {
        var (lida, _) = ArquivoLicenca.Ler(Encoding.UTF8.GetBytes("não sou uma licença"), _publica);
        Assert.Null(lida);
    }

    // ---------------------------------------------------------------- serviço

    [Fact]
    public void Sem_arquivo_fica_em_demonstracao_e_so_abre_em_localhost_local()
    {
        var servico = Servico();
        Assert.Equal(SituacaoLicenca.Demonstracao, servico.Estado.Situacao);
        Assert.True(servico.HostAutorizado("localhost", requisicaoLocal: true));
        Assert.False(servico.HostAutorizado("localhost", requisicaoLocal: false));
        Assert.False(servico.HostAutorizado("pradoshop.com.br", requisicaoLocal: false));
    }

    [Fact]
    public async Task Licenca_instalada_libera_so_os_dominios_dela_e_sobrevive_ao_reinicio()
    {
        var servico = Servico();
        var r = await servico.InstalarAsync(ArquivoLicenca.Emitir(Dados(), _chave));
        Assert.True(r.Sucesso, r.Erro);

        Assert.True(servico.HostAutorizado("www.pradoshop.com.br", false));
        Assert.True(servico.HostAutorizado("PRADOSHOP.com.br", false));
        Assert.False(servico.HostAutorizado("outraloja.com.br", false));
        Assert.False(servico.HostAutorizado("pradoshop.com.br.outraloja.com", false));

        var reiniciado = Servico();
        Assert.Equal(SituacaoLicenca.Valida, reiniciado.Estado.Situacao);
        Assert.Equal("Prado Shop", reiniciado.Estado.Dados!.NomeLoja);
    }

    [Fact]
    public async Task Licenca_adulterada_nao_substitui_a_valida()
    {
        var servico = Servico();
        await servico.InstalarAsync(ArquivoLicenca.Emitir(Dados(), _chave));
        using var outra = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var r = await servico.InstalarAsync(ArquivoLicenca.Emitir(Dados(null, "loja-pirata.com.br"), outra));

        Assert.False(r.Sucesso);
        Assert.False(servico.HostAutorizado("loja-pirata.com.br", false));
        Assert.True(servico.HostAutorizado("pradoshop.com.br", false));
    }

    [Fact]
    public async Task Licenca_vencida_bloqueia_a_vitrine()
    {
        var servico = Servico();
        await servico.InstalarAsync(ArquivoLicenca.Emitir(Dados(_relogio.AgoraUtc.AddDays(10)), _chave));
        Assert.True(servico.HostAutorizado("pradoshop.com.br", false));

        _relogio.Avancar(TimeSpan.FromDays(11));

        Assert.Equal(SituacaoLicenca.Expirada, servico.Estado.Situacao);
        Assert.False(servico.HostAutorizado("pradoshop.com.br", false));
    }

    [Fact]
    public async Task Licenca_ja_vencida_nao_e_instalada()
    {
        var r = await Servico().InstalarAsync(ArquivoLicenca.Emitir(Dados(_relogio.AgoraUtc.AddDays(-1)), _chave));
        Assert.False(r.Sucesso);
    }

    [Fact]
    public void Arquivo_adulterado_no_disco_deixa_a_licenca_invalida()
    {
        var caminho = Path.Combine(_pasta, "licenca.lic");
        File.WriteAllText(caminho, "{\"dados\":\"e30=\",\"assinatura\":\"AAAA\"}");
        var servico = Servico(caminho);
        Assert.Equal(SituacaoLicenca.Invalida, servico.Estado.Situacao);
        Assert.False(servico.HostAutorizado("pradoshop.com.br", false));
    }

    [Theory]
    [InlineData("*.pradoshop.com.br", "loja.pradoshop.com.br", true)]
    [InlineData("*.pradoshop.com.br", "pradoshop.com.br", false)]
    [InlineData("*.pradoshop.com.br", "xpradoshop.com.br", false)]
    [InlineData("pradoshop.com.br", "pradoshop.com.br", true)]
    public void Dominio_aceita_curinga_so_para_subdominios(string licenciado, string host, bool esperado) =>
        Assert.Equal(esperado, ServicoLicenca.DominioConfere(licenciado, host));
}
