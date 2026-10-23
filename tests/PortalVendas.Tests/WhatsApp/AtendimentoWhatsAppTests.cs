using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Contas;
using PortalVendas.Application.WhatsApp;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Domain.WhatsApp;
using PortalVendas.Tests.Apoio;

namespace PortalVendas.Tests.WhatsApp;

public class AtendimentoWhatsAppTests
{
    private const string Telefone = "5519988887777";

    /// <summary>Simula o cliente: grava a conversa e chama o robô como o processador faria.</summary>
    private sealed class Cliente
    {
        private readonly Cenario _c;
        private int _seq;
        public ConversaWhatsApp Conversa { get; }

        public Cliente(Cenario c)
        {
            _c = c;
            Conversa = new ConversaWhatsApp { Telefone = Telefone, NomeContato = "Maria", CriadoEm = c.Relogio.AgoraUtc, UltimaMensagemEm = c.Relogio.AgoraUtc };
            c.Db.ConversasWhatsApp.Add(Conversa);
            c.Db.SaveChanges();
        }

        public async Task<IReadOnlyList<MensagemSaida>> DizerAsync(string texto, string? opcao = null, bool expirado = false)
        {
            var r = await _c.Atendimento.ResponderAsync(Conversa,
                new MensagemRecebida(Telefone, "Maria", texto, opcao, "t" + ++_seq, _c.Relogio.AgoraUtc), expirado);
            await _c.Db.SaveChangesAsync();
            return r;
        }

        public Task<IReadOnlyList<MensagemSaida>> TocarAsync(string opcao) => DizerAsync("(toque)", opcao);
    }

    private static string Tudo(IEnumerable<MensagemSaida> msgs) => string.Join("\n---\n", msgs.Select(m => m.TextoComOpcoes()));

    [Fact]
    public async Task Saudacao_mostra_o_menu_com_as_opcoes()
    {
        using var c = new Cenario();
        var cliente = new Cliente(c);

        var r = await cliente.DizerAsync("Oi!");

        var menu = Assert.Single(r);
        Assert.Contains("Loja Teste", menu.Texto);
        Assert.Equal(5, menu.Opcoes!.Count);
        Assert.Equal(EtapaConversa.Menu, cliente.Conversa.Etapa);
    }

    [Fact]
    public async Task Compra_completa_pelo_whatsapp_com_pix_gera_pedido_de_origem_whatsapp()
    {
        using var c = new Cenario();
        var produto = await c.CriarProdutoAsync(estoque: 10, preco: 80m, nome: "Fone Bluetooth");
        var cliente = new Cliente(c);

        await cliente.DizerAsync("oi");
        var categorias = await cliente.DizerAsync("1");                     // número digitado → "Ver produtos"
        Assert.Contains(categorias.Last().Opcoes!, o => o.Id == "cat:geral");

        var produtos = await cliente.TocarAsync("cat:geral");
        Assert.Contains(produtos.Last().Opcoes!, o => o.Id == "prod:" + produto.Slug);

        var detalhe = await cliente.TocarAsync("prod:" + produto.Slug);
        Assert.Contains("Fone Bluetooth", detalhe.Last().Texto);
        Assert.Contains(detalhe.Last().Opcoes!, o => o.Id == "add:" + produto.Slug);

        await cliente.TocarAsync("add:" + produto.Slug);
        Assert.Equal(EtapaConversa.InformandoQuantidade, cliente.Conversa.Etapa);
        var adicionado = await cliente.DizerAsync("2");
        Assert.Contains("Adicionado: 2x Fone Bluetooth", Tudo(adicionado));

        await cliente.TocarAsync("car:finalizar");
        Assert.Equal(EtapaConversa.InformandoNome, cliente.Conversa.Etapa);
        Assert.Contains("CPF", Tudo(await cliente.DizerAsync("Maria Oliveira")));
        await cliente.DizerAsync("529.982.247-25");
        Assert.Equal(EtapaConversa.InformandoEmail, cliente.Conversa.Etapa);
        await cliente.TocarAsync("pular");

        // Conta criada sem senha, vinculada à conversa; carrinho anônimo foi para a conta
        var usuario = await c.Db.Usuarios.AsNoTracking().SingleAsync(u => u.Cpf == "52998224725");
        Assert.Equal(ContaService.MarcadorContaWhatsApp, usuario.SenhaHash);
        Assert.Equal("19988887777", usuario.Telefone);
        Assert.Equal(usuario.Id, cliente.Conversa.UsuarioId);
        Assert.Equal(EtapaConversa.InformandoCep, cliente.Conversa.Etapa);

        await cliente.DizerAsync("13084-000");
        await cliente.DizerAsync("120");
        var fretes = await cliente.TocarAsync("pular");
        Assert.Equal(EtapaConversa.EscolhendoFrete, cliente.Conversa.Etapa);
        var opcaoFrete = fretes.Last().Opcoes!.First();
        Assert.StartsWith("frete:", opcaoFrete.Id);

        var resumo = await cliente.TocarAsync(opcaoFrete.Id);
        Assert.Contains("Total", resumo.Last().Texto);

        var pagamento = await cliente.TocarAsync("pag:pix");
        var texto = Tudo(pagamento);
        Assert.Contains("00020126580014br.gov.bcb.pix-teste", texto);           // copia e cola numa mensagem própria
        Assert.Contains("https://teste/conta/acesso-whatsapp?token=", texto);   // link para cartão/boleto
        Assert.Equal(EtapaConversa.AguardandoPagamento, cliente.Conversa.Etapa);

        var pedido = await c.Db.Pedidos.AsNoTracking().Include(p => p.Itens).SingleAsync();
        Assert.Equal(OrigemPedido.WhatsApp, pedido.Origem);
        Assert.Equal(usuario.Id, pedido.UsuarioId);
        Assert.Equal(160m, pedido.Subtotal);
        Assert.Equal(StatusPedido.AguardandoPagamento, pedido.Status);

        // Tocar de novo em "Pagar com Pix" não cria outro pedido
        await cliente.TocarAsync("pag:pix");
        Assert.Equal(1, await c.Db.Pedidos.CountAsync());
    }

    [Fact]
    public async Task Cliente_que_volta_pelo_mesmo_numero_e_reconhecido()
    {
        using var c = new Cenario();
        var produto = await c.CriarProdutoAsync();
        var cliente = new Cliente(c);
        await cliente.TocarAsync("add:" + produto.Slug);
        await cliente.DizerAsync("1");
        await cliente.TocarAsync("car:finalizar");
        await cliente.DizerAsync("Maria Oliveira");
        await cliente.DizerAsync("52998224725");
        await cliente.TocarAsync("pular");
        var usuarioId = cliente.Conversa.UsuarioId;

        // Conversa apagada (ou outro aparelho): ao escrever de novo, o número identifica a conta criada no WhatsApp
        c.Db.ConversasWhatsApp.Remove(cliente.Conversa);
        await c.Db.SaveChangesAsync();
        var novamente = new Cliente(c);
        await novamente.DizerAsync("oi");
        Assert.Equal(usuarioId, novamente.Conversa.UsuarioId);
    }

    [Fact]
    public async Task Cpf_de_conta_do_site_nao_e_vinculado_a_conversa()
    {
        using var c = new Cenario();
        await c.CriarClienteAsync(cpf: "52998224725");
        var produto = await c.CriarProdutoAsync();
        var cliente = new Cliente(c);
        await cliente.TocarAsync("add:" + produto.Slug);
        await cliente.DizerAsync("1");
        await cliente.TocarAsync("car:finalizar");
        await cliente.DizerAsync("Maria Oliveira");

        var r = await cliente.DizerAsync("52998224725");

        Assert.Contains("já tem cadastro", Tudo(r));
        Assert.Null(cliente.Conversa.UsuarioId);
        Assert.Equal(1, await c.Db.Usuarios.CountAsync());
    }

    [Fact]
    public async Task Conta_do_site_com_o_mesmo_telefone_nao_e_vinculada_automaticamente()
    {
        using var c = new Cenario();
        var site = await c.CriarClienteAsync();
        site.Telefone = "19988887777";
        await c.Db.SaveChangesAsync();
        var cliente = new Cliente(c);

        await cliente.DizerAsync("oi");

        Assert.Null(cliente.Conversa.UsuarioId);   // o telefone do cadastro do site não é verificado
    }

    [Fact]
    public async Task Dados_invalidos_sao_pedidos_de_novo()
    {
        using var c = new Cenario();
        var produto = await c.CriarProdutoAsync();
        var cliente = new Cliente(c);
        await cliente.TocarAsync("add:" + produto.Slug);
        await cliente.DizerAsync("1");
        await cliente.TocarAsync("car:finalizar");

        Assert.Contains("nome e sobrenome", Tudo(await cliente.DizerAsync("Ma")));
        await cliente.DizerAsync("Maria Oliveira");
        Assert.Contains("não parece válido", Tudo(await cliente.DizerAsync("111.111.111-11")));
        Assert.Equal(EtapaConversa.InformandoCpf, cliente.Conversa.Etapa);
    }

    [Fact]
    public async Task Cancelar_no_meio_do_cadastro_volta_ao_menu_e_mantem_o_carrinho()
    {
        using var c = new Cenario();
        var produto = await c.CriarProdutoAsync();
        var cliente = new Cliente(c);
        await cliente.TocarAsync("add:" + produto.Slug);
        await cliente.DizerAsync("3");
        await cliente.TocarAsync("car:finalizar");

        var r = await cliente.DizerAsync("cancelar");

        Assert.Equal(EtapaConversa.Menu, cliente.Conversa.Etapa);
        Assert.Contains(r.Last().Opcoes!, o => o.Titulo == "Meu carrinho (3)");
    }

    [Fact]
    public async Task Texto_livre_sem_ia_busca_no_catalogo()
    {
        using var c = new Cenario();
        var fone = await c.CriarProdutoAsync(nome: "Fone Bluetooth");
        await c.CriarProdutoAsync(nome: "Garrafa Térmica");
        var cliente = new Cliente(c);

        var r = await cliente.DizerAsync("Fone");   // no SQL Server a busca ignora maiúsculas (collation CI_AI); no SQLite dos testes, não

        Assert.Equal(EtapaConversa.EscolhendoProduto, cliente.Conversa.Etapa);
        var opcoes = r.Last().Opcoes!;
        Assert.Contains(opcoes, o => o.Id == "prod:" + fone.Slug);
        Assert.DoesNotContain(opcoes, o => o.Titulo == "Garrafa Térmica");
    }

    [Fact]
    public async Task Ia_entende_a_busca_e_o_robo_mostra_os_produtos_do_catalogo()
    {
        using var c = new Cenario();
        var fone = await c.CriarProdutoAsync(nome: "Fone Bluetooth");
        c.Assistente.Proxima = new InterpretacaoIA(IntencaoCliente.Buscar, "Fone", null);
        var cliente = new Cliente(c);

        var r = await cliente.DizerAsync("vcs tem algum fone sem fio pra academia?");

        Assert.Equal(1, c.Assistente.Chamadas);
        Assert.Contains(r.Last().Opcoes!, o => o.Id == "prod:" + fone.Slug);
    }

    [Fact]
    public async Task Ia_responde_duvida_e_oferece_opcoes()
    {
        using var c = new Cenario();
        c.Assistente.Proxima = new InterpretacaoIA(IntencaoCliente.Duvida, null, "Aceitamos Pix, cartão em até 12x e boleto.");
        var cliente = new Cliente(c);

        var r = await cliente.DizerAsync("quais as formas de pagamento?");

        Assert.Contains("Aceitamos Pix", r.Single().Texto);
        Assert.Contains(r.Single().Opcoes!, o => o.Id == "menu:atendente");
    }

    [Fact]
    public async Task Pedir_atendente_passa_a_conversa_para_humano()
    {
        using var c = new Cenario();
        var cliente = new Cliente(c);

        await cliente.DizerAsync("quero falar com um atendente");

        Assert.True(cliente.Conversa.AtendimentoHumano);
    }

    [Fact]
    public async Task Audio_recebe_aviso_de_que_o_robo_so_entende_texto()
    {
        using var c = new Cenario();
        var cliente = new Cliente(c);

        var r = await cliente.DizerAsync("[áudio]");

        Assert.Contains("só mensagens de texto", r.Single().Texto);
    }

    [Fact]
    public async Task Contexto_expirado_recomeca_pelo_menu()
    {
        using var c = new Cenario();
        var produto = await c.CriarProdutoAsync();
        var cliente = new Cliente(c);
        await cliente.TocarAsync("add:" + produto.Slug);
        Assert.Equal(EtapaConversa.InformandoQuantidade, cliente.Conversa.Etapa);

        await cliente.DizerAsync("2", expirado: true);   // "2" depois de horas não é quantidade: vira escolha no menu

        Assert.NotEqual(EtapaConversa.Carrinho, cliente.Conversa.Etapa);
    }
}
