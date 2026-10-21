using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Contas;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Tests.Apoio;

namespace PortalVendas.Tests.Aplicacao;

public class ContaServiceTests
{
    private static ComandoCadastro Cadastro(string email = "novo@teste.com.br", string cpf = "390.533.447-05") =>
        new("João Pereira", email, cpf, "(19) 98888-7777", "SenhaBoa123", AceitouTermos: true);

    private static string TokenDoEmail(string html) =>
        Uri.UnescapeDataString(Regex.Match(html, @"token=([^""&<]+)").Groups[1].Value);

    [Fact]
    public async Task Cadastro_cria_conta_nao_confirmada_e_envia_link()   // RF05
    {
        using var c = new Cenario();
        var r = await c.Contas.CadastrarAsync(Cadastro());

        Assert.True(r.Sucesso, r.Erro);
        var u = await c.Db.Usuarios.SingleAsync(x => x.Email == "novo@teste.com.br");
        Assert.False(u.EmailConfirmado);
        Assert.Equal("39053344705", u.Cpf);
        Assert.NotEqual("SenhaBoa123", u.SenhaHash);
        var email = Assert.Single(c.Email.Enviados);
        Assert.Equal("Confirme seu e-mail", email.Assunto);

        var confirmacao = await c.Contas.ConfirmarEmailAsync(TokenDoEmail(email.Html));
        Assert.True(confirmacao.Sucesso);
        Assert.False((await c.Contas.ConfirmarEmailAsync(TokenDoEmail(email.Html))).Sucesso);   // uso único
    }

    [Theory]
    [InlineData("390.533.447-04", "CPF inválido.")]
    [InlineData("390.533.447-05", null)]
    public async Task Cadastro_valida_cpf(string cpf, string? erro)
    {
        using var c = new Cenario();
        var r = await c.Contas.CadastrarAsync(Cadastro(cpf: cpf));
        Assert.Equal(erro, r.Erro);
    }

    [Fact]
    public async Task Email_ja_cadastrado_responde_igual_e_avisa_o_dono()   // RF05: não revela se o e-mail existe
    {
        using var c = new Cenario();
        var existente = await c.CriarClienteAsync(email: "ja@teste.com.br");

        var r = await c.Contas.CadastrarAsync(Cadastro(email: "JA@teste.com.br"));

        Assert.True(r.Sucesso);
        Assert.Equal(1, await c.Db.Usuarios.CountAsync(u => u.Email == "ja@teste.com.br"));
        Assert.Contains(c.Email.Enviados, e => e.Para == existente.Email && e.Assunto.Contains("Tentativa de cadastro"));
    }

    [Fact]
    public async Task Bloqueia_por_15_minutos_apos_5_erros()   // RF05
    {
        using var c = new Cenario();
        var u = await c.CriarClienteAsync();

        for (var i = 0; i < 4; i++)
            Assert.Equal(SituacaoLogin.CredenciaisInvalidas, (await c.Contas.AutenticarAsync(u.Email, "errada")).Situacao);
        Assert.Equal(SituacaoLogin.Bloqueado, (await c.Contas.AutenticarAsync(u.Email, "errada")).Situacao);

        // Nem a senha certa entra durante o bloqueio
        Assert.Equal(SituacaoLogin.Bloqueado, (await c.Contas.AutenticarAsync(u.Email, Cenario.SenhaPadrao)).Situacao);

        c.Relogio.Avancar(TimeSpan.FromMinutes(16));
        Assert.Equal(SituacaoLogin.Sucesso, (await c.Contas.AutenticarAsync(u.Email, Cenario.SenhaPadrao)).Situacao);
    }

    [Fact]
    public async Task Email_inexistente_tem_a_mesma_resposta_de_senha_errada()
    {
        using var c = new Cenario();
        var r = await c.Contas.AutenticarAsync("ninguem@teste.com.br", "qualquer");
        Assert.Equal(SituacaoLogin.CredenciaisInvalidas, r.Situacao);
        Assert.Null(r.Usuario);
    }

    [Fact]
    public async Task Redefinicao_de_senha_troca_o_carimbo_e_o_link_vale_uma_vez()   // RF06
    {
        using var c = new Cenario();
        var u = await c.CriarClienteAsync();
        var carimboAntes = u.CarimboSeguranca;

        await c.Contas.SolicitarRedefinicaoSenhaAsync(u.Email);
        var token = TokenDoEmail(c.Email.Enviados.Single(e => e.Assunto == "Redefinição de senha").Html);

        Assert.True((await c.Contas.RedefinirSenhaAsync(token, "NovaSenha456")).Sucesso);
        Assert.False((await c.Contas.RedefinirSenhaAsync(token, "OutraSenha789")).Sucesso);

        c.Db.ChangeTracker.Clear();
        var depois = await c.Db.Usuarios.SingleAsync(x => x.Id == u.Id);
        Assert.NotEqual(carimboAntes, depois.CarimboSeguranca);   // sessões antigas caem
        Assert.False(await c.Contas.SessaoValidaAsync(u.Id, carimboAntes));
        Assert.Equal(SituacaoLogin.Sucesso, (await c.Contas.AutenticarAsync(u.Email, "NovaSenha456")).Situacao);
    }

    [Fact]
    public async Task Link_de_redefinicao_expira_em_1_hora()
    {
        using var c = new Cenario();
        var u = await c.CriarClienteAsync();
        await c.Contas.SolicitarRedefinicaoSenhaAsync(u.Email);
        var token = TokenDoEmail(c.Email.Enviados.Single().Html);

        c.Relogio.Avancar(TimeSpan.FromMinutes(61));

        Assert.False((await c.Contas.RedefinirSenhaAsync(token, "NovaSenha456")).Sucesso);
    }

    [Fact]
    public async Task Administrador_exige_segundo_fator()   // RNF05
    {
        using var c = new Cenario();
        Assert.True((await c.Contas.CriarAdministradorAsync("admin@loja.com.br", "Admin", "SenhaMuitoForte1")).Sucesso);

        var primeiro = await c.Contas.AutenticarAsync("admin@loja.com.br", "SenhaMuitoForte1");
        Assert.Equal(SituacaoLogin.RequerConfiguracaoTotp, primeiro.Situacao);

        var adminId = primeiro.Usuario!.Id;
        var segredo = await c.Contas.PrepararTotpAsync(adminId);
        var codigo = Totp.Calcular(Totp.Base32Decodificar(segredo!), Totp.Contador(c.Relogio.AgoraUtc));
        Assert.True((await c.Contas.AtivarTotpAsync(adminId, codigo)).Sucesso);

        var segundo = await c.Contas.AutenticarAsync("admin@loja.com.br", "SenhaMuitoForte1");
        Assert.Equal(SituacaoLogin.RequerCodigoTotp, segundo.Situacao);
        Assert.Equal(SituacaoLogin.CredenciaisInvalidas, (await c.Contas.VerificarTotpAsync(adminId, "000000")).Situacao);
        Assert.Equal(SituacaoLogin.Sucesso, (await c.Contas.VerificarTotpAsync(adminId, codigo)).Situacao);
    }

    [Fact]
    public async Task Exclusao_lgpd_anonimiza_e_preserva_pedidos()   // RNF07
    {
        using var c = new Cenario();
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();
        await c.Pedidos.CancelarPeloClienteAsync(cliente.Id, pedidoId);

        var r = await c.Contas.AnonimizarAsync(cliente.Id, Cenario.SenhaPadrao);

        Assert.True(r.Sucesso, r.Erro);
        c.Db.ChangeTracker.Clear();
        var u = await c.Db.Usuarios.SingleAsync(x => x.Id == cliente.Id);
        Assert.True(u.Anonimizado);
        Assert.Null(u.Cpf);
        Assert.DoesNotContain("@teste", u.Email);
        Assert.Equal(0, await c.Db.Enderecos.CountAsync(e => e.UsuarioId == cliente.Id));
        Assert.Equal(StatusPedido.Cancelado, (await c.RecarregarPedidoAsync(pedidoId)).Status);   // pedido continua lá
        Assert.Equal(SituacaoLogin.CredenciaisInvalidas, (await c.Contas.AutenticarAsync(cliente.Email, Cenario.SenhaPadrao)).Situacao);
    }

    [Fact]
    public async Task Exclusao_bloqueada_com_pedido_em_andamento()
    {
        using var c = new Cenario();
        var (cliente, _, _) = await c.CriarPedidoAsync();
        var r = await c.Contas.AnonimizarAsync(cliente.Id, Cenario.SenhaPadrao);
        Assert.False(r.Sucesso);
    }
}
