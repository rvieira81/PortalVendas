using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Application.WhatsApp;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Infrastructure.WhatsApp;
using PortalVendas.Tests.Apoio;

namespace PortalVendas.Tests.WhatsApp;

public class IntegracaoWhatsAppTests
{
    // ---------------------------------------------------------------- webhooks

    private const string Meta = """
        {"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"messages","value":{
          "messaging_product":"whatsapp","metadata":{"phone_number_id":"123"},
          "contacts":[{"profile":{"name":"Maria"},"wa_id":"5519988887777"}],
          "messages":[
            {"from":"5519988887777","id":"wamid.A","timestamp":"1760000000","type":"text","text":{"body":"oi"}},
            {"from":"5519988887777","id":"wamid.B","timestamp":"1760000001","type":"interactive",
             "interactive":{"type":"list_reply","list_reply":{"id":"cat:casa","title":"Casa"}}},
            {"from":"5519988887777","id":"wamid.C","timestamp":"1760000002","type":"audio","audio":{"id":"x"}}
          ]}}]}]}
        """;

    [Fact]
    public void Webhook_da_meta_le_texto_lista_e_audio()
    {
        using var json = JsonDocument.Parse(Meta);
        var m = LeitorWebhook.LerMeta(json.RootElement);

        Assert.Equal(3, m.Count);
        Assert.Equal(("5519988887777", "Maria", "oi", (string?)null, "meta:wamid.A"), (m[0].Telefone, m[0].Nome, m[0].Texto, m[0].IdOpcao, m[0].IdExterno));
        Assert.Equal("cat:casa", m[1].IdOpcao);
        Assert.Equal("[áudio]", m[2].Texto);
    }

    [Fact]
    public void Webhook_da_meta_sem_mensagens_status_de_entrega_e_ignorado()
    {
        using var json = JsonDocument.Parse("""{"entry":[{"changes":[{"value":{"statuses":[{"id":"x","status":"read"}]}}]}]}""");
        Assert.Empty(LeitorWebhook.LerMeta(json.RootElement));
    }

    [Fact]
    public void Assinatura_da_meta_confere_o_hmac_do_corpo()
    {
        var corpo = Encoding.UTF8.GetBytes(Meta);
        var assinatura = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("segredo"), corpo)).ToLowerInvariant();

        Assert.True(LeitorWebhook.AssinaturaMetaValida(corpo, assinatura, "segredo"));
        Assert.False(LeitorWebhook.AssinaturaMetaValida(corpo, assinatura, "outro"));
        Assert.False(LeitorWebhook.AssinaturaMetaValida(Encoding.UTF8.GetBytes(Meta + " "), assinatura, "segredo"));
        Assert.False(LeitorWebhook.AssinaturaMetaValida(corpo, null, "segredo"));
        Assert.False(LeitorWebhook.AssinaturaMetaValida(corpo, assinatura, null));
    }

    [Fact]
    public void Webhook_da_evolution_ignora_eco_e_grupos_e_resolve_lid()
    {
        using var json = JsonDocument.Parse("""
            {"event":"messages.upsert","instance":"loja","data":[
              {"key":{"remoteJid":"5519988887777@s.whatsapp.net","fromMe":false,"id":"E1"},"pushName":"Maria",
               "message":{"conversation":"quero um fone"},"messageTimestamp":1760000000},
              {"key":{"remoteJid":"5519988887777@s.whatsapp.net","fromMe":true,"id":"E2"},"message":{"conversation":"resposta da loja"}},
              {"key":{"remoteJid":"120363@g.us","fromMe":false,"id":"E3"},"message":{"conversation":"grupo"}},
              {"key":{"remoteJid":"98765@lid","senderPn":"5511977776666@s.whatsapp.net","fromMe":false,"id":"E4"},
               "message":{"extendedTextMessage":{"text":"2"}}}
            ]}
            """);
        var m = LeitorWebhook.LerEvolution(json.RootElement);

        Assert.Equal(2, m.Count);
        Assert.Equal(("5519988887777", "quero um fone", "evo:E1"), (m[0].Telefone, m[0].Texto, m[0].IdExterno));
        Assert.Equal(("5511977776666", "2"), (m[1].Telefone, m[1].Texto));
    }

    [Theory]
    [InlineData("""{"intencao":"buscar","busca":"panela de pressão","resposta":null}""", IntencaoCliente.Buscar, "panela de pressão")]
    [InlineData("Claro! ```json\n{\"intencao\": \"duvida\", \"resposta\": \"Entregamos em todo o Brasil.\"}\n```", IntencaoCliente.Duvida, null)]
    [InlineData("""{"intencao":"qualquer coisa"}""", IntencaoCliente.Outro, null)]
    public void Resposta_da_ia_e_lida_mesmo_com_texto_em_volta(string saida, IntencaoCliente intencao, string? busca)
    {
        var r = AssistenteClaude.Interpretar(saida);
        Assert.NotNull(r);
        Assert.Equal(intencao, r.Intencao);
        Assert.Equal(busca, r.Busca);
    }

    [Fact]
    public async Task Simulador_aceita_tudo_sem_enviar()
    {
        var conector = new ConectorSimulador();
        var r = await conector.EnviarAsync("5511900000001", MensagemSaida.Simples("oi"));
        Assert.True(r.Sucesso);
        Assert.StartsWith("sim:", r.IdExterno);
        Assert.Equal(ProvedorWhatsApp.Simulador, conector.Provedor);
    }

    [Fact]
    public async Task Configuracao_aceita_o_simulador_sem_credenciais()
    {
        using var c = new Cenario();
        var r = await c.ConfiguracaoWhatsApp.SalvarAsync(new EdicaoConfiguracaoWhatsApp(ProvedorWhatsApp.Simulador, true, null,
            null, null, null, null, null, null, null, null, false, null, null), "admin");
        Assert.True(r.Sucesso, r.Erro);
        Assert.Equal(ProvedorWhatsApp.Simulador, (await c.ConfiguracaoWhatsApp.ObterAsync()).Provedor);
    }

    [Fact]
    public void Resposta_da_ia_sem_json_vira_null() => Assert.Null(AssistenteClaude.Interpretar("não sei"));

    [Fact]
    public void Opcoes_viram_lista_numerada_no_texto()
    {
        var m = new MensagemSaida("Escolha:", new[] { new OpcaoMenu("a", "Ver produtos"), new OpcaoMenu("b", "Carrinho", "2 itens") });
        Assert.Contains("1. Ver produtos", m.TextoComOpcoes());
        Assert.Contains("2. Carrinho — 2 itens", m.TextoComOpcoes());
    }

    [Theory]
    [InlineData("(19) 98888-7777", "5519988887777")]
    [InlineData("+55 19 98888-7777", "5519988887777")]
    [InlineData("1933334444", "551933334444")]
    public void Telefone_ganha_ddi_do_brasil(string entrada, string esperado) => Assert.Equal(esperado, TelefoneWhatsApp.Normalizar(entrada));

    // ---------------------------------------------------------------- licença

    [Fact]
    public void Modulo_so_vale_com_licenca_valida_que_o_inclua()
    {
        Assert.True(LicencaFalsa.Valida("whatsapp").Estado.TemModulo(ModulosLicenca.WhatsApp));
        Assert.True(LicencaFalsa.Valida("WhatsApp").Estado.TemModulo(ModulosLicenca.WhatsApp));
        Assert.False(LicencaFalsa.Valida().Estado.TemModulo(ModulosLicenca.WhatsApp));
        Assert.False(new LicencaFalsa().Estado.TemModulo(ModulosLicenca.WhatsApp));   // demonstração
    }

    // ---------------------------------------------------------------- processamento ponta a ponta

    private static async Task ConfigurarEvolutionAsync(Cenario c)
    {
        var r = await c.ConfiguracaoWhatsApp.SalvarAsync(new EdicaoConfiguracaoWhatsApp(ProvedorWhatsApp.Evolution, true, null,
            null, null, null, null, null, "https://evolution.teste", "loja", "chave-secreta", false, null, null), "admin");
        Assert.True(r.Sucesso, r.Erro);
    }

    private static MensagemRecebida Msg(string texto, string id) =>
        new("5519988887777", "Maria", texto, null, id, new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Mensagem_repetida_pelo_provedor_e_gravada_uma_vez_so()
    {
        using var c = new Cenario();
        var primeira = await c.ProcessadorWhatsApp.RegistrarEntradaAsync(Msg("oi", "evo:1"));
        var repetida = await c.ProcessadorWhatsApp.RegistrarEntradaAsync(Msg("oi", "evo:1"));

        Assert.NotNull(primeira);
        Assert.Null(repetida);
        Assert.Equal(1, await c.Db.MensagensWhatsApp.CountAsync());
    }

    [Fact]
    public async Task Mensagem_recebida_e_respondida_e_o_historico_fica_no_painel()
    {
        using var c = new Cenario();
        await ConfigurarEvolutionAsync(c);

        var id = await c.ProcessadorWhatsApp.RegistrarEntradaAsync(Msg("oi", "evo:1"));
        await c.ProcessadorWhatsApp.ProcessarAsync(id!.Value);

        var enviada = Assert.Single(c.Conector.Enviadas);
        Assert.Equal("5519988887777", enviada.Telefone);
        var conversa = await c.Db.ConversasWhatsApp.AsNoTracking().SingleAsync();
        var detalhe = await c.ConversasWhatsApp.AbrirAsync(conversa.Id);
        Assert.Equal(2, detalhe!.Mensagens.Count);
        Assert.True(detalhe.Mensagens[0].Entrada);
        Assert.Equal(EnvioWhatsAppService.AutorRobo, detalhe.Mensagens[1].Autor);
        Assert.True((await c.Db.MensagensWhatsApp.AsNoTracking().FirstAsync(m => m.Id == id)).Processada);
    }

    [Fact]
    public async Task Com_atendente_na_conversa_o_robo_nao_responde()
    {
        using var c = new Cenario();
        await ConfigurarEvolutionAsync(c);
        var id = await c.ProcessadorWhatsApp.RegistrarEntradaAsync(Msg("oi", "evo:1"));
        var conversa = await c.Db.ConversasWhatsApp.SingleAsync();
        await c.ConversasWhatsApp.AssumirAsync(conversa.Id);

        await c.ProcessadorWhatsApp.ProcessarAsync(id!.Value);
        Assert.Empty(c.Conector.Enviadas);

        var r = await c.ConversasWhatsApp.ResponderAsync(conversa.Id, "Oi Maria, aqui é a Ana!", "ana@loja.com.br");
        Assert.True(r.Sucesso, r.Erro);
        Assert.Equal("Oi Maria, aqui é a Ana!", Assert.Single(c.Conector.Enviadas).Mensagem.Texto);
    }

    [Fact]
    public async Task Sem_o_modulo_na_licenca_as_mensagens_nao_sao_respondidas()
    {
        using var c = new Cenario();
        await ConfigurarEvolutionAsync(c);
        c.Licenca.Estado = LicencaFalsa.Valida().Estado;   // licença sem o módulo

        var id = await c.ProcessadorWhatsApp.RegistrarEntradaAsync(Msg("oi", "evo:1"));
        await c.ProcessadorWhatsApp.ProcessarAsync(id!.Value);

        Assert.Empty(c.Conector.Enviadas);
        Assert.False((await c.ConfiguracaoWhatsApp.SalvarAsync(new EdicaoConfiguracaoWhatsApp(ProvedorWhatsApp.Desativado, true, null,
            null, null, null, null, null, null, null, null, false, null, null), "admin")).Sucesso);
    }

    [Fact]
    public async Task Segredos_ficam_cifrados_no_banco()
    {
        using var c = new Cenario();
        await ConfigurarEvolutionAsync(c);

        var gravado = await c.Db.Configuracoes.AsNoTracking().SingleAsync(x => x.Chave == "WhatsApp.Evolution.ApiKey");
        Assert.DoesNotContain("chave-secreta", gravado.Valor);
        Assert.Equal("chave-secreta", (await c.ConfiguracaoWhatsApp.ObterAsync()).EvolutionApiKey);
    }

    [Fact]
    public async Task Pagamento_confirmado_avisa_o_cliente_na_conversa()
    {
        using var c = new Cenario();
        await ConfigurarEvolutionAsync(c);
        var (cliente, _, pedidoId) = await c.CriarPedidoAsync();
        c.Db.ConversasWhatsApp.Add(new Domain.WhatsApp.ConversaWhatsApp
        {
            Telefone = "5519988887777", UsuarioId = cliente.Id, CriadoEm = c.Relogio.AgoraUtc,
            UltimaMensagemEm = c.Relogio.AgoraUtc, UltimaEntradaEm = c.Relogio.AgoraUtc
        });
        await c.Db.SaveChangesAsync();
        var pedido = await c.Db.Pedidos.FirstAsync(p => p.Id == pedidoId);
        pedido.ConfirmarPagamento(c.Relogio.AgoraUtc, "teste", "Pix");
        await c.Db.SaveChangesAsync();

        var aviso = new AvisoPedidoWhatsApp(c.Db, c.ConfiguracaoWhatsApp, c.EnvioWhatsApp, c.Identidade,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AvisoPedidoWhatsApp>.Instance);
        await aviso.AvisarAsync(pedidoId);

        Assert.Contains("confirmado", Assert.Single(c.Conector.Enviadas).Mensagem.Texto);
    }

    [Fact]
    public void Aviso_de_envio_traz_o_rastreio()
    {
        using var c = new Cenario();
        var pedido = Pedido.Criar(1, Guid.NewGuid(), new[] { (1, "Fone", "SKU", 50m, 1) },
            new EnderecoEntrega("Maria", "13084000", "Rua", "1", null, "Centro", "Campinas", "SP"), 1, "PAC", 10m, 5, 0m, c.Relogio.AgoraUtc, "teste");
        pedido.ConfirmarPagamento(c.Relogio.AgoraUtc, "t", "ok");
        pedido.IniciarSeparacao(c.Relogio.AgoraUtc, "t");
        pedido.MarcarEnviado("BR123456789BR", c.Relogio.AgoraUtc, "t");

        Assert.Contains("BR123456789BR", AvisoPedidoWhatsApp.Texto(pedido));
        Assert.Null(AvisoPedidoWhatsApp.Texto(Pedido.Criar(1, Guid.NewGuid(), new[] { (1, "Fone", "SKU", 50m, 1) },
            new EnderecoEntrega("Maria", "13084000", "Rua", "1", null, "Centro", "Campinas", "SP"), 1, "PAC", 10m, 5, 0m, c.Relogio.AgoraUtc, "teste")));
    }
}
