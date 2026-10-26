using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Carrinhos;
using PortalVendas.Application.Catalogo;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Contas;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Application.Pagamentos;
using PortalVendas.Application.Pedidos;
using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Domain.Usuarios;
using PortalVendas.Domain.WhatsApp;

namespace PortalVendas.Application.WhatsApp;

/// <summary>Estado do robô guardado na conversa (JSON na coluna Contexto).</summary>
public sealed class EstadoConversa
{
    /// <summary>Ids das opções mostradas por último, na ordem: o cliente pode responder "2".</summary>
    public List<string> Opcoes { get; set; } = new();
    public Guid? CarrinhoToken { get; set; }
    public string? ProdutoSlug { get; set; }
    public string? Nome { get; set; }
    public string? Cpf { get; set; }
    public string? Cep { get; set; }
    public string? Logradouro { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Numero { get; set; }
    public int? EnderecoId { get; set; }
    public string? FreteCodigo { get; set; }
    public Guid? ChavePedido { get; set; }
    public int? PedidoId { get; set; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static EstadoConversa Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new EstadoConversa();
        try { return JsonSerializer.Deserialize<EstadoConversa>(json, Json) ?? new EstadoConversa(); }
        catch (JsonException) { return new EstadoConversa(); }
    }

    public string Gravar() => JsonSerializer.Serialize(this, Json);

    /// <summary>Recomeça o atendimento mantendo o carrinho (o cliente não perde o que escolheu).</summary>
    public void Recomecar()
    {
        var carrinho = CarrinhoToken;
        Opcoes = new();
        ProdutoSlug = Nome = Cpf = Cep = Logradouro = Bairro = Cidade = Uf = Numero = FreteCodigo = null;
        EnderecoId = PedidoId = null;
        ChavePedido = null;
        CarrinhoToken = carrinho;
    }
}

/// <summary>
/// O robô de vendas: menu de categorias e produtos, carrinho, cadastro rápido, endereço, frete e pagamento
/// (Pix na conversa ou link para cartão/boleto no site). Mensagens livres passam pela IA (quando ligada)
/// ou pela busca do catálogo. Não envia nada: devolve as mensagens e quem chama envia e grava.
/// </summary>
public sealed class AtendimentoWhatsAppService
{
    private const int MaximoOpcoesLista = 9;   // a lista da Meta aceita 10 linhas: 9 itens + "Menu"
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly HashSet<string> PalavrasMenu = new(StringComparer.OrdinalIgnoreCase)
    {
        "oi", "ola", "olá", "menu", "inicio", "início", "voltar", "bom dia", "boa tarde", "boa noite", "opa", "e ai", "e aí", "hello", "hi"
    };
    private static readonly HashSet<string> PalavrasCancelar = new(StringComparer.OrdinalIgnoreCase) { "cancelar", "sair", "parar", "menu", "voltar" };
    private static readonly HashSet<string> PalavrasPular = new(StringComparer.OrdinalIgnoreCase) { "pular", "nao", "não", "sem", "nenhum", "-", "n" };

    private readonly IAppDbContext _db;
    private readonly CatalogoService _catalogo;
    private readonly CarrinhoService _carrinhos;
    private readonly CheckoutService _checkout;
    private readonly PagamentoService _pagamentos;
    private readonly EnderecoService _enderecos;
    private readonly ContaService _contas;
    private readonly ICepService _cep;
    private readonly ConfiguracaoLojaService _configuracaoLoja;
    private readonly IdentidadeLojaService _identidade;
    private readonly ConfiguracaoWhatsAppService _configuracao;
    private readonly IAssistenteVendas _assistente;
    private readonly ILinksAplicacao _links;
    private readonly ILicencaLoja _licenca;
    private readonly IRelogio _relogio;
    private readonly ILogger<AtendimentoWhatsAppService> _logger;

    public AtendimentoWhatsAppService(IAppDbContext db, CatalogoService catalogo, CarrinhoService carrinhos, CheckoutService checkout,
        PagamentoService pagamentos, EnderecoService enderecos, ContaService contas, ICepService cep,
        ConfiguracaoLojaService configuracaoLoja, IdentidadeLojaService identidade, ConfiguracaoWhatsAppService configuracao,
        IAssistenteVendas assistente, ILinksAplicacao links, ILicencaLoja licenca, IRelogio relogio, ILogger<AtendimentoWhatsAppService> logger)
    {
        _db = db;
        _catalogo = catalogo;
        _carrinhos = carrinhos;
        _checkout = checkout;
        _pagamentos = pagamentos;
        _enderecos = enderecos;
        _contas = contas;
        _cep = cep;
        _configuracaoLoja = configuracaoLoja;
        _identidade = identidade;
        _configuracao = configuracao;
        _assistente = assistente;
        _links = links;
        _licenca = licenca;
        _relogio = relogio;
        _logger = logger;
    }

    /// <summary>Processa uma mensagem do cliente e devolve as respostas. Altera a conversa (etapa, contexto, cliente).</summary>
    public async Task<IReadOnlyList<MensagemSaida>> ResponderAsync(ConversaWhatsApp conversa, MensagemRecebida mensagem, bool contextoExpirado,
        CancellationToken ct = default)
    {
        var estado = EstadoConversa.Ler(conversa.Contexto);
        if (contextoExpirado && conversa.Etapa != EtapaConversa.AguardandoPagamento)
        {
            estado.Recomecar();
            conversa.Etapa = EtapaConversa.Menu;
        }

        var turno = new Turno(conversa, estado, mensagem);
        await VincularClienteAsync(turno, ct);
        await DecidirAsync(turno, ct);

        // A numeração vale para as opções da última mensagem que tinha opções
        var comOpcoes = turno.Saida.LastOrDefault(m => m.Opcoes is { Count: > 0 });
        estado.Opcoes = comOpcoes?.Opcoes!.Select(o => o.Id).ToList() ?? new List<string>();
        conversa.Contexto = estado.Gravar();
        return turno.Saida;
    }

    private sealed class Turno
    {
        public Turno(ConversaWhatsApp conversa, EstadoConversa estado, MensagemRecebida mensagem)
        {
            Conversa = conversa;
            Estado = estado;
            Mensagem = mensagem;
            Texto = (mensagem.Texto ?? string.Empty).Trim();
        }

        public ConversaWhatsApp Conversa { get; }
        public EstadoConversa Estado { get; }
        public MensagemRecebida Mensagem { get; }
        public string Texto { get; }
        public List<MensagemSaida> Saida { get; } = new();
        public int? UsuarioId => Conversa.UsuarioId;

        public IdentificacaoCarrinho Carrinho => UsuarioId is not null
            ? new IdentificacaoCarrinho(null, UsuarioId)
            : new IdentificacaoCarrinho(Estado.CarrinhoToken, null);

        public void Dizer(string texto, IReadOnlyList<OpcaoMenu>? opcoes = null, string? imagem = null, string? tituloLista = null) =>
            Saida.Add(new MensagemSaida(texto, opcoes, imagem, tituloLista));

        public void Etapa(EtapaConversa etapa) => Conversa.Etapa = etapa;
    }

    // ===================================================================== roteamento

    private static readonly HashSet<EtapaConversa> EtapasDeDados = new()
    {
        EtapaConversa.InformandoQuantidade, EtapaConversa.Buscando, EtapaConversa.InformandoNome, EtapaConversa.InformandoCpf,
        EtapaConversa.InformandoEmail, EtapaConversa.InformandoCep, EtapaConversa.InformandoNumero, EtapaConversa.InformandoComplemento
    };

    private async Task DecidirAsync(Turno t, CancellationToken ct)
    {
        // 1) Toque em botão/lista, ou número digitado numa etapa de escolha
        var opcao = t.Mensagem.IdOpcao;
        if (opcao is null && !EtapasDeDados.Contains(t.Conversa.Etapa)
            && int.TryParse(t.Texto.TrimEnd('.', ')'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n >= 1 && n <= t.Estado.Opcoes.Count)
            opcao = t.Estado.Opcoes[n - 1];

        if (opcao is not null && await ExecutarOpcaoAsync(t, opcao, ct)) return;

        // Áudio, foto sem legenda, figurinha, localização: o robô só entende texto
        if (t.Texto.StartsWith('[') && t.Texto.IndexOf(']') is var fim and > 0 && t.Texto[(fim + 1)..].Trim().Length == 0)
        {
            t.Dizer("Por enquanto eu entendo só mensagens de texto. 🙂 Escreva o que precisa ou toque em uma opção.",
                new[] { new OpcaoMenu("menu", "Menu"), new OpcaoMenu("menu:atendente", "Falar com atendente") });
            return;
        }

        var normalizado = Normalizar(t.Texto);

        // 2) Etapas em que o cliente digita um dado
        if (EtapasDeDados.Contains(t.Conversa.Etapa))
        {
            if (PalavrasCancelar.Contains(normalizado))
            {
                await MostrarMenuAsync(t, ct, "Tudo bem, parei por aqui. Seu carrinho continua guardado.");
                return;
            }
            await ReceberDadoAsync(t, ct);
            return;
        }

        // 3) Texto livre: palavras-chave, IA e busca
        if (string.IsNullOrEmpty(normalizado) || PalavrasMenu.Contains(normalizado))
        {
            await MostrarMenuAsync(t, ct);
            return;
        }
        if (normalizado.Contains("atendente") || normalizado.Contains("humano") || normalizado.Contains("falar com alguem") || normalizado.Contains("falar com uma pessoa"))
        {
            await ChamarAtendenteAsync(t, ct);
            return;
        }
        if (normalizado is "carrinho" or "meu carrinho" or "ver carrinho")
        {
            await MostrarCarrinhoAsync(t, ct);
            return;
        }
        if (normalizado is "finalizar" or "fechar pedido" or "finalizar compra" or "comprar")
        {
            await FinalizarAsync(t, ct);
            return;
        }
        if (normalizado.StartsWith("meus pedidos", StringComparison.Ordinal) || normalizado is "pedido" or "pedidos" or "rastreio" or "rastrear")
        {
            await MostrarPedidosAsync(t, ct);
            return;
        }
        if (t.Conversa.Etapa == EtapaConversa.AguardandoPagamento && normalizado is "pix" or "copia e cola" or "codigo")
        {
            await ReenviarPixAsync(t, ct);
            return;
        }

        await TextoLivreAsync(t, ct);
    }

    /// <summary>Opções têm o formato "acao" ou "acao:valor" e valem em qualquer etapa (o cliente pode tocar num menu antigo).</summary>
    private async Task<bool> ExecutarOpcaoAsync(Turno t, string opcao, CancellationToken ct)
    {
        var (acao, valor) = opcao.IndexOf(':') is var i and > 0 ? (opcao[..i], opcao[(i + 1)..]) : (opcao, "");
        switch (acao)
        {
            case "menu":
                switch (valor)
                {
                    case "produtos": await MostrarCategoriasAsync(t, ct); return true;
                    case "buscar":
                        t.Etapa(EtapaConversa.Buscando);
                        t.Dizer("O que você procura? Escreva o nome do produto (ex.: *fone bluetooth*).");
                        return true;
                    case "carrinho": await MostrarCarrinhoAsync(t, ct); return true;
                    case "pedidos": await MostrarPedidosAsync(t, ct); return true;
                    case "atendente": await ChamarAtendenteAsync(t, ct); return true;
                    default: await MostrarMenuAsync(t, ct); return true;
                }
            case "cat":
                await MostrarProdutosAsync(t, null, valor, ct);
                return true;
            case "prod":
                await MostrarProdutoAsync(t, valor, ct);
                return true;
            case "add":
                await PedirQuantidadeAsync(t, valor, ct);
                return true;
            case "qtd":
                if (int.TryParse(valor, out var q)) await AdicionarAsync(t, q, ct);
                return true;
            case "car":
                switch (valor)
                {
                    case "finalizar": await FinalizarAsync(t, ct); return true;
                    case "limpar": await EsvaziarCarrinhoAsync(t, ct); return true;
                    default: await MostrarCategoriasAsync(t, ct); return true;
                }
            case "pular":
                if (t.Conversa.Etapa is EtapaConversa.InformandoEmail or EtapaConversa.InformandoComplemento)
                {
                    await ReceberDadoAsync(t, ct, pular: true);
                    return true;
                }
                return false;
            case "end":
                if (valor == "novo") { PedirCep(t); return true; }
                if (int.TryParse(valor, out var enderecoId)) { await UsarEnderecoAsync(t, enderecoId, ct); return true; }
                return false;
            case "frete":
                await EscolherFreteAsync(t, valor, ct);
                return true;
            case "pag":
                switch (valor)
                {
                    case "pix": await FecharPedidoAsync(t, pix: true, ct); return true;
                    case "link": await FecharPedidoAsync(t, pix: false, ct); return true;
                    case "repix": await ReenviarPixAsync(t, ct); return true;
                    default: await MostrarMenuAsync(t, ct, "Pedido não finalizado. Seu carrinho continua guardado."); return true;
                }
            default:
                return false;
        }
    }

    // ===================================================================== menu e catálogo

    private async Task MostrarMenuAsync(Turno t, CancellationToken ct, string? aviso = null)
    {
        var loja = await _identidade.ObterOuPadraoAsync(ct);
        var config = await _configuracao.ObterAsync(ct);
        var itens = await _carrinhos.ContarItensAsync(t.Carrinho, ct);
        var nome = PrimeiroNome(t.Conversa.NomeContato);

        var texto = new StringBuilder();
        if (aviso is not null) texto.Append(aviso).Append("\n\n");
        texto.Append(!string.IsNullOrWhiteSpace(config.BoasVindas)
            ? config.BoasVindas.Replace("{nome}", nome ?? "", StringComparison.OrdinalIgnoreCase).Replace("{loja}", loja.NomeLoja, StringComparison.OrdinalIgnoreCase)
            : $"Olá{(nome is null ? "" : ", " + nome)}! 👋 Aqui é o atendimento da *{loja.NomeLoja}*.\nComo posso ajudar? Você também pode escrever o que procura.");

        t.Etapa(EtapaConversa.Menu);
        t.Dizer(texto.ToString(), new[]
        {
            new OpcaoMenu("menu:produtos", "Ver produtos", "Navegar pelas categorias"),
            new OpcaoMenu("menu:buscar", "Buscar produto", "Diga o que procura"),
            new OpcaoMenu("menu:carrinho", itens > 0 ? $"Meu carrinho ({itens})" : "Meu carrinho"),
            new OpcaoMenu("menu:pedidos", "Meus pedidos", "Status e rastreio"),
            new OpcaoMenu("menu:atendente", "Falar com atendente")
        }, tituloLista: "Ver opções");
    }

    private async Task MostrarCategoriasAsync(Turno t, CancellationToken ct)
    {
        var categorias = await _catalogo.ListarCategoriasAsync(ct);
        if (categorias.Count == 0)
        {
            await MostrarMenuAsync(t, ct, "Ainda não temos produtos cadastrados. Volte em breve!");
            return;
        }
        var opcoes = categorias.Take(MaximoOpcoesLista).Select(c => new OpcaoMenu("cat:" + c.Slug, c.Nome)).ToList();
        opcoes.Add(new OpcaoMenu("menu", "Menu principal"));
        t.Etapa(EtapaConversa.EscolhendoCategoria);
        t.Dizer(categorias.Count > MaximoOpcoesLista
                ? "Escolha uma categoria — ou escreva o nome do produto que procura:"
                : "Escolha uma categoria:",
            opcoes, tituloLista: "Categorias");
    }

    private async Task MostrarProdutosAsync(Turno t, string? busca, string? categoriaSlug, CancellationToken ct)
    {
        var pagina = await _catalogo.ListarVitrineAsync(new FiltroVitrine(busca, categoriaSlug, TamanhoPagina: MaximoOpcoesLista,
            Ordem: string.IsNullOrEmpty(busca) ? OrdemVitrine.Recentes : OrdemVitrine.Nome), ct);
        if (pagina.Itens.Count == 0)
        {
            t.Etapa(EtapaConversa.Buscando);
            t.Dizer(busca is null
                ? "Essa categoria está sem produtos no momento."
                : $"Não encontrei nada para \"{Cortar(busca, 60)}\". Tente outra palavra ou toque em Menu.",
                new[] { new OpcaoMenu("menu:produtos", "Ver categorias"), new OpcaoMenu("menu", "Menu") });
            return;
        }

        var opcoes = pagina.Itens
            .Select(p => new OpcaoMenu("prod:" + p.Slug, p.Nome, p.Esgotado ? "Esgotado" : Moeda(p.Preco)))
            .ToList();
        opcoes.Add(new OpcaoMenu("menu", "Menu principal"));

        string titulo;
        if (busca is not null) titulo = $"Encontrei {pagina.TotalItens} {(pagina.TotalItens == 1 ? "produto" : "produtos")} para \"{Cortar(busca, 60)}\":";
        else
        {
            var categoria = (await _catalogo.ListarCategoriasAsync(ct)).FirstOrDefault(c => c.Slug == categoriaSlug)?.Nome ?? "a categoria";
            titulo = $"Produtos de *{categoria}*:";
        }
        if (pagina.TotalItens > MaximoOpcoesLista) titulo += $"\n(mostrando {MaximoOpcoesLista} de {pagina.TotalItens} — escreva o nome para refinar)";

        t.Etapa(EtapaConversa.EscolhendoProduto);
        t.Dizer(titulo, opcoes, tituloLista: "Ver produtos");
    }

    private async Task MostrarProdutoAsync(Turno t, string slug, CancellationToken ct)
    {
        var p = slug.Length <= 220 ? await _catalogo.ObterPorSlugAsync(slug, ct) : null;
        if (p is null)
        {
            await MostrarMenuAsync(t, ct, "Esse produto não está mais disponível.");
            return;
        }
        var configuracao = await _configuracaoLoja.ObterAsync(ct);
        t.Estado.ProdutoSlug = p.Slug;

        var texto = new StringBuilder()
            .Append('*').Append(p.Nome).Append("*\n");
        if (p.PrecoDe is { } de && de > p.Preco) texto.Append("~").Append(Moeda(de)).Append("~ ");
        texto.Append("*").Append(Moeda(p.Preco)).Append("*\n");
        var parcelas = configuracao.ParcelasPermitidas(p.Preco);
        if (parcelas > 1) texto.Append("ou ").Append(parcelas).Append("x de ").Append(Moeda(Math.Round(p.Preco / parcelas, 2))).Append(" sem juros\n");
        if (!string.IsNullOrWhiteSpace(p.Descricao)) texto.Append('\n').Append(Cortar(p.Descricao.Trim(), 350)).Append('\n');
        texto.Append('\n').Append(p.Esgotado ? "❌ Esgotado no momento." : p.Disponivel <= 5 ? $"⚠️ Restam {p.Disponivel} unidades." : "✅ Em estoque.");

        var opcoes = new List<OpcaoMenu>();
        if (!p.Esgotado) opcoes.Add(new OpcaoMenu("add:" + p.Slug, "Adicionar"));
        opcoes.Add(new OpcaoMenu("cat:" + p.Categoria.Slug, "Ver outros"));
        opcoes.Add(new OpcaoMenu("menu", "Menu"));

        t.Etapa(EtapaConversa.VendoProduto);
        t.Dizer(texto.ToString(), opcoes, imagem: p.Imagens.Count > 0 ? _links.Imagem(p.Imagens[0]) : null);
    }

    // ===================================================================== carrinho

    private async Task PedirQuantidadeAsync(Turno t, string slug, CancellationToken ct)
    {
        var p = slug.Length <= 220 ? await _catalogo.ObterPorSlugAsync(slug, ct) : null;
        if (p is null || p.Esgotado)
        {
            await MostrarMenuAsync(t, ct, "Esse produto acabou de esgotar. 😕");
            return;
        }
        t.Estado.ProdutoSlug = p.Slug;
        var maximo = Math.Min(p.QuantidadeMaximaCompra, p.Disponivel);
        t.Etapa(EtapaConversa.InformandoQuantidade);
        t.Dizer($"Quantas unidades de *{p.Nome}*? (de 1 a {maximo})",
            Enumerable.Range(1, Math.Min(3, maximo)).Select(q => new OpcaoMenu("qtd:" + q, q.ToString(CultureInfo.InvariantCulture))).ToList());
    }

    private async Task AdicionarAsync(Turno t, int quantidade, CancellationToken ct)
    {
        var p = t.Estado.ProdutoSlug is { } slug ? await _catalogo.ObterPorSlugAsync(slug, ct) : null;
        if (p is null)
        {
            await MostrarMenuAsync(t, ct, "Não consegui identificar o produto. Escolha de novo, por favor.");
            return;
        }
        var maximo = Math.Min(p.QuantidadeMaximaCompra, Math.Max(p.Disponivel, 1));
        if (quantidade < 1 || quantidade > maximo)
        {
            t.Dizer($"Informe uma quantidade de 1 a {maximo}.");
            return;
        }

        var r = await _carrinhos.AdicionarAsync(t.Carrinho, p.Id, quantidade, ct);
        if (!r.Sucesso)
        {
            await MostrarMenuAsync(t, ct, r.Erro);
            return;
        }
        if (t.UsuarioId is null) t.Estado.CarrinhoToken = r.Valor;

        var carrinho = await _carrinhos.MontarViewAsync(t.Carrinho, ct);
        var texto = $"✅ Adicionado: {quantidade}x {p.Nome}." + (r.Erro is null ? "" : $"\n⚠️ {r.Erro}")
                    + $"\nSubtotal do carrinho: *{Moeda(carrinho?.Subtotal ?? 0)}*";
        t.Etapa(EtapaConversa.Carrinho);
        t.Dizer(texto, new[]
        {
            new OpcaoMenu("car:finalizar", "Finalizar compra"),
            new OpcaoMenu("car:continuar", "Continuar comprando"),
            new OpcaoMenu("menu:carrinho", "Ver carrinho")
        });
    }

    private async Task MostrarCarrinhoAsync(Turno t, CancellationToken ct)
    {
        var carrinho = await _carrinhos.MontarViewAsync(t.Carrinho, ct);
        if (carrinho is null || carrinho.EstaVazio)
        {
            t.Etapa(EtapaConversa.Menu);
            t.Dizer("Seu carrinho está vazio.", new[] { new OpcaoMenu("menu:produtos", "Ver produtos"), new OpcaoMenu("menu", "Menu") });
            return;
        }
        var sb = new StringBuilder("🛒 *Seu carrinho*\n");
        foreach (var i in carrinho.Itens)
            sb.Append("\n• ").Append(i.Quantidade).Append("x ").Append(i.Nome).Append(" — ")
              .Append(i.Indisponivel ? "indisponível" : Moeda(i.Total));
        sb.Append("\n\nSubtotal: *").Append(Moeda(carrinho.Subtotal)).Append('*');
        foreach (var aviso in carrinho.Avisos) sb.Append("\n⚠️ ").Append(aviso);

        t.Etapa(EtapaConversa.Carrinho);
        t.Dizer(sb.ToString(), new[]
        {
            new OpcaoMenu("car:finalizar", "Finalizar compra"),
            new OpcaoMenu("car:continuar", "Continuar comprando"),
            new OpcaoMenu("car:limpar", "Esvaziar carrinho"),
            new OpcaoMenu("menu", "Menu principal")
        }, tituloLista: "Opções");
    }

    private async Task EsvaziarCarrinhoAsync(Turno t, CancellationToken ct)
    {
        var carrinho = await _carrinhos.MontarViewAsync(t.Carrinho, ct);
        foreach (var i in carrinho?.Itens ?? Array.Empty<ItemCarrinhoView>())
            await _carrinhos.RemoverAsync(t.Carrinho, i.ProdutoId, ct);
        await MostrarMenuAsync(t, ct, "Carrinho esvaziado.");
    }

    // ===================================================================== cadastro, endereço e frete

    private async Task FinalizarAsync(Turno t, CancellationToken ct)
    {
        var carrinho = await _carrinhos.MontarViewAsync(t.Carrinho, ct);
        if (carrinho is null || carrinho.EstaVazio)
        {
            await MostrarCarrinhoAsync(t, ct);
            return;
        }
        if (!carrinho.PodeFinalizar)
        {
            t.Dizer("Tem item indisponível no carrinho. Remova e tente de novo.");
            await MostrarCarrinhoAsync(t, ct);
            return;
        }

        if (t.UsuarioId is null)
        {
            var loja = await _identidade.ObterOuPadraoAsync(ct);
            t.Etapa(EtapaConversa.InformandoNome);
            t.Dizer("Para fechar o pedido preciso de alguns dados (é rapidinho e só na primeira compra).\n\n" +
                    $"Ao continuar você concorda com os termos de uso e a política de privacidade da {loja.NomeLoja}: {_links.Inicio()}privacidade\n\n" +
                    "Qual é o seu *nome completo*?");
            return;
        }
        await EscolherEnderecoAsync(t, ct);
    }

    private async Task EscolherEnderecoAsync(Turno t, CancellationToken ct)
    {
        var enderecos = await _enderecos.ListarAsync(t.UsuarioId!.Value, ct);
        if (enderecos.Count == 0)
        {
            PedirCep(t);
            return;
        }
        var opcoes = enderecos.OrderByDescending(e => e.Padrao).Take(MaximoOpcoesLista - 1)
            .Select(e => new OpcaoMenu("end:" + e.Id.ToString(CultureInfo.InvariantCulture), $"{e.Logradouro}, {e.Numero}",
                $"{e.Bairro} — {e.Cidade}/{e.Uf}"))
            .ToList();
        opcoes.Add(new OpcaoMenu("end:novo", "Outro endereço"));
        t.Etapa(EtapaConversa.EscolhendoEndereco);
        t.Dizer("Onde vamos entregar?", opcoes, tituloLista: "Endereços");
    }

    private static void PedirCep(Turno t)
    {
        t.Etapa(EtapaConversa.InformandoCep);
        t.Dizer("Qual é o *CEP* de entrega? (só os 8 números)");
    }

    private async Task UsarEnderecoAsync(Turno t, int enderecoId, CancellationToken ct)
    {
        if (t.UsuarioId is null || await _enderecos.ObterAsync(t.UsuarioId.Value, enderecoId, ct) is null)
        {
            await MostrarMenuAsync(t, ct);
            return;
        }
        t.Estado.EnderecoId = enderecoId;
        await CotarFreteAsync(t, ct);
    }

    private async Task CotarFreteAsync(Turno t, CancellationToken ct)
    {
        var endereco = await _enderecos.ObterAsync(t.UsuarioId!.Value, t.Estado.EnderecoId!.Value, ct);
        if (endereco is null)
        {
            PedirCep(t);
            return;
        }
        var cotacao = await _checkout.CotarFreteAsync(t.UsuarioId.Value, endereco.Cep, ct);
        if (!cotacao.Sucesso || cotacao.Valor!.Count == 0)
        {
            t.Etapa(EtapaConversa.Menu);
            t.Dizer($"Não consegui calcular o frete para o CEP {Domain.Comum.Cep.Formatar(endereco.Cep)}: {cotacao.Erro ?? "sem opções de entrega"}",
                new[] { new OpcaoMenu("menu:atendente", "Falar com atendente"), new OpcaoMenu("menu", "Menu") });
            return;
        }
        var opcoes = cotacao.Valor.Take(MaximoOpcoesLista)
            .Select(o => new OpcaoMenu("frete:" + o.Codigo, o.Descricao, $"{(o.Gratis ? "Grátis" : Moeda(o.Valor))} · até {Prazo(o.PrazoDias)}"))
            .ToList();
        t.Etapa(EtapaConversa.EscolhendoFrete);
        t.Dizer($"🚚 Entrega em: {endereco.Resumo}\n\nEscolha o frete:", opcoes, tituloLista: "Opções de frete");
    }

    private async Task EscolherFreteAsync(Turno t, string codigo, CancellationToken ct)
    {
        if (t.UsuarioId is null || t.Estado.EnderecoId is null)
        {
            await FinalizarAsync(t, ct);
            return;
        }
        var carrinho = await _carrinhos.MontarViewAsync(t.Carrinho, ct);
        var endereco = await _enderecos.ObterAsync(t.UsuarioId.Value, t.Estado.EnderecoId.Value, ct);
        var cotacao = endereco is null ? null : await _checkout.CotarFreteAsync(t.UsuarioId.Value, endereco.Cep, ct);
        var frete = cotacao?.Valor?.FirstOrDefault(o => o.Codigo == codigo);
        if (carrinho is null || carrinho.EstaVazio || frete is null)
        {
            t.Dizer("Essa opção de frete não está mais disponível. Vou calcular de novo.");
            await CotarFreteAsync(t, ct);
            return;
        }

        t.Estado.FreteCodigo = codigo;
        t.Estado.ChavePedido ??= Guid.NewGuid();   // idempotência: tocar duas vezes em "Pagar" não cria dois pedidos

        var sb = new StringBuilder("🧾 *Resumo do pedido*\n");
        foreach (var i in carrinho.Itens) sb.Append("\n• ").Append(i.Quantidade).Append("x ").Append(i.Nome).Append(" — ").Append(Moeda(i.Total));
        sb.Append("\n\nProdutos: ").Append(Moeda(carrinho.Subtotal))
          .Append("\nFrete (").Append(frete.Descricao).Append("): ").Append(frete.Gratis ? "Grátis" : Moeda(frete.Valor))
          .Append("\n*Total: ").Append(Moeda(carrinho.Subtotal + frete.Valor)).Append('*')
          .Append("\n\nEntrega em até ").Append(Prazo(frete.PrazoDias)).Append(" após a confirmação do pagamento.")
          .Append("\n\nComo prefere pagar?");

        t.Etapa(EtapaConversa.ConfirmandoPedido);
        t.Dizer(sb.ToString(), new[]
        {
            new OpcaoMenu("pag:pix", "Pagar com Pix"),
            new OpcaoMenu("pag:link", "Cartão ou boleto"),
            new OpcaoMenu("pag:cancelar", "Cancelar")
        });
    }

    // ===================================================================== pedido e pagamento

    private async Task FecharPedidoAsync(Turno t, bool pix, CancellationToken ct)
    {
        if (t.UsuarioId is null || t.Estado.EnderecoId is null || t.Estado.FreteCodigo is null || t.Estado.ChavePedido is null)
        {
            // Pedido já criado nesta conversa (toque repetido num botão antigo): reaproveita
            if (t.Estado.PedidoId is not null && t.UsuarioId is not null)
            {
                await EnviarPagamentoAsync(t, t.Estado.PedidoId.Value, pix, ct);
                return;
            }
            await FinalizarAsync(t, ct);
            return;
        }

        var r = await _checkout.CriarPedidoAsync(new ComandoCriarPedido(t.UsuarioId.Value, t.Estado.ChavePedido.Value,
            t.Estado.EnderecoId.Value, t.Estado.FreteCodigo, OrigemPedido.WhatsApp), ct);
        if (!r.Sucesso)
        {
            t.Estado.ChavePedido = null;
            t.Etapa(EtapaConversa.Carrinho);
            t.Dizer("Não consegui fechar o pedido: " + r.Erro, new[]
            {
                new OpcaoMenu("menu:carrinho", "Ver carrinho"),
                new OpcaoMenu("menu:atendente", "Falar com atendente"),
                new OpcaoMenu("menu", "Menu")
            });
            return;
        }

        var pedidoId = r.Valor;
        t.Estado.PedidoId = pedidoId;
        t.Estado.ChavePedido = null;
        t.Estado.FreteCodigo = null;
        _logger.LogInformation("Pedido {PedidoId} criado pelo WhatsApp (conversa {ConversaId})", pedidoId, t.Conversa.Id);
        await EnviarPagamentoAsync(t, pedidoId, pix, ct);
    }

    private async Task EnviarPagamentoAsync(Turno t, int pedidoId, bool pix, CancellationToken ct)
    {
        var usuarioId = t.UsuarioId!.Value;
        var pedido = await _db.Pedidos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pedidoId && p.UsuarioId == usuarioId, ct);
        if (pedido is null)
        {
            await MostrarMenuAsync(t, ct);
            return;
        }
        if (!pedido.PodeReceberPagamento)
        {
            await MostrarMenuAsync(t, ct, $"O pedido nº {pedido.Numero} está: {pedido.Status.Descricao()}.");
            return;
        }

        var link = _links.AcessoWhatsApp(await _contas.CriarAcessoWhatsAppAsync(usuarioId, ct), pedidoId);
        t.Etapa(EtapaConversa.AguardandoPagamento);

        if (pix)
        {
            var pagamento = await _pagamentos.GerarPixAsync(usuarioId, pedidoId, ct);
            if (pagamento.Sucesso && !string.IsNullOrEmpty(pagamento.Valor!.PixQrCode))
            {
                var validade = pagamento.Valor.ExpiraEm is { } expira
                    ? Horario.ParaBrasilia(expira).ToString("HH:mm", PtBr)
                    : null;
                t.Dizer($"✅ Pedido nº *{pedido.Numero}* registrado! Total: *{Moeda(pedido.Total)}*.\n\n" +
                        $"Copie o código abaixo e pague no app do seu banco (Pix copia e cola){(validade is null ? "" : $" até as {validade}")}:");
                t.Dizer(pagamento.Valor.PixQrCode!);
                t.Dizer("Assim que o pagamento for confirmado eu aviso aqui. 💚\n\n" +
                        $"Prefere cartão ou boleto? Pague por este link (vale 24 h):\n{link}",
                    new[] { new OpcaoMenu("pag:repix", "Ver o Pix de novo"), new OpcaoMenu("menu", "Menu") });
                return;
            }
            t.Dizer("Não consegui gerar o Pix agora. Você pode pagar pelo link abaixo (Pix, cartão ou boleto):");
        }
        else
        {
            t.Dizer($"✅ Pedido nº *{pedido.Numero}* registrado! Total: *{Moeda(pedido.Total)}*.");
        }
        t.Dizer($"Pague com cartão (até {(await _configuracaoLoja.ObterAsync(ct)).ParcelasPermitidas(pedido.Total)}x), boleto ou Pix por este link seguro (vale 24 h):\n{link}\n\n" +
                "Quando o pagamento for confirmado eu aviso aqui.",
            new[] { new OpcaoMenu("menu:pedidos", "Meus pedidos"), new OpcaoMenu("menu", "Menu") });
    }

    private async Task ReenviarPixAsync(Turno t, CancellationToken ct)
    {
        if (t.UsuarioId is null || t.Estado.PedidoId is null)
        {
            await MostrarPedidosAsync(t, ct);
            return;
        }
        await EnviarPagamentoAsync(t, t.Estado.PedidoId.Value, pix: true, ct);
    }

    private async Task MostrarPedidosAsync(Turno t, CancellationToken ct)
    {
        if (t.UsuarioId is null)
        {
            await MostrarMenuAsync(t, ct, "Você ainda não tem pedidos por aqui.");
            return;
        }
        var pedidos = await _db.Pedidos.AsNoTracking()
            .Where(p => p.UsuarioId == t.UsuarioId)
            .OrderByDescending(p => p.CriadoEm)
            .Take(5)
            .Select(p => new { p.Id, p.CriadoEm, p.Status, p.Total, p.CodigoRastreio })
            .ToListAsync(ct);
        if (pedidos.Count == 0)
        {
            await MostrarMenuAsync(t, ct, "Você ainda não tem pedidos.");
            return;
        }
        var sb = new StringBuilder("📦 *Seus últimos pedidos*\n");
        foreach (var p in pedidos)
        {
            sb.Append("\n*nº ").Append(p.Id.ToString("D6", CultureInfo.InvariantCulture)).Append("* — ")
              .Append(Horario.ParaBrasilia(p.CriadoEm).ToString("dd/MM", PtBr)).Append(" — ").Append(Moeda(p.Total))
              .Append("\n").Append(p.Status.Descricao());
            if (!string.IsNullOrEmpty(p.CodigoRastreio)) sb.Append(" — rastreio ").Append(p.CodigoRastreio);
            sb.Append('\n');
        }
        var opcoes = new List<OpcaoMenu>();
        var aguardando = pedidos.FirstOrDefault(p => p.Status == StatusPedido.AguardandoPagamento);
        if (aguardando is not null)
        {
            t.Estado.PedidoId = aguardando.Id;
            opcoes.Add(new OpcaoMenu("pag:repix", $"Pagar nº {aguardando.Id:D6}"));
        }
        opcoes.Add(new OpcaoMenu("menu:atendente", "Falar com atendente"));
        opcoes.Add(new OpcaoMenu("menu", "Menu"));
        t.Etapa(EtapaConversa.MeusPedidos);
        t.Dizer(sb.ToString().TrimEnd(), opcoes);
    }

    private async Task ChamarAtendenteAsync(Turno t, CancellationToken ct)
    {
        var loja = await _identidade.ObterOuPadraoAsync(ct);
        t.Conversa.AtendimentoHumano = true;
        t.Etapa(EtapaConversa.Menu);
        t.Dizer("Certo! Já avisei a equipe: um atendente vai continuar a conversa por aqui." +
                (string.IsNullOrEmpty(loja.Horario) ? "" : $"\nNosso horário de atendimento: {loja.Horario}."));
    }

    // ===================================================================== dados digitados

    private async Task ReceberDadoAsync(Turno t, CancellationToken ct, bool pular = false)
    {
        var texto = t.Texto;
        var e = t.Estado;
        switch (t.Conversa.Etapa)
        {
            case EtapaConversa.Buscando:
                if (texto.Length < 2)
                {
                    t.Dizer("Escreva pelo menos 2 letras do que você procura.");
                    return;
                }
                await MostrarProdutosAsync(t, Cortar(texto, 100), null, ct);
                return;

            case EtapaConversa.InformandoQuantidade:
                if (int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)) await AdicionarAsync(t, q, ct);
                else t.Dizer("Responda só com o número de unidades (ex.: 2).");
                return;

            case EtapaConversa.InformandoNome:
                var nome = Texto.Limpar(texto);
                if (nome is null || nome.Length is < 5 or > 150 || !nome.Contains(' ') || nome.Any(char.IsDigit))
                {
                    t.Dizer("Preciso do nome e sobrenome (ex.: Maria Silva).");
                    return;
                }
                e.Nome = nome;
                t.Etapa(EtapaConversa.InformandoCpf);
                t.Dizer($"Obrigado, {PrimeiroNome(nome)}! Agora o seu *CPF* (só números).\nEle é exigido para a cobrança e a nota fiscal.");
                return;

            case EtapaConversa.InformandoCpf:
                var cpf = Cpf.Normalizar(texto);
                if (!Cpf.EhValido(cpf))
                {
                    t.Dizer("Esse CPF não parece válido. Confira e digite de novo (11 números).");
                    return;
                }
                if (await _db.Usuarios.AnyAsync(u => u.Cpf == cpf, ct))
                {
                    // Por segurança não vinculamos a conversa a uma conta já existente
                    t.Etapa(EtapaConversa.Menu);
                    t.Dizer("Este CPF já tem cadastro na loja. Por segurança, finalize a compra pelo site ou peça ajuda a um atendente.\n" + _links.Inicio(),
                        new[] { new OpcaoMenu("menu:atendente", "Falar com atendente"), new OpcaoMenu("menu", "Menu") });
                    return;
                }
                e.Cpf = cpf;
                t.Etapa(EtapaConversa.InformandoEmail);
                t.Dizer("Quer receber o comprovante por *e-mail*? Digite o e-mail ou toque em Pular.",
                    new[] { new OpcaoMenu("pular", "Pular") });
                return;

            case EtapaConversa.InformandoEmail:
                string? email = null;
                if (!pular && !PalavrasPular.Contains(Normalizar(texto)))
                {
                    email = texto.Trim().ToLowerInvariant();
                    if (email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out var endereco) || endereco.Address != email || !email.Contains('.'))
                    {
                        t.Dizer("E-mail inválido. Digite de novo ou toque em Pular.", new[] { new OpcaoMenu("pular", "Pular") });
                        return;
                    }
                }
                if (!await CriarClienteAsync(t, email, ct)) return;
                await EscolherEnderecoAsync(t, ct);
                return;

            case EtapaConversa.InformandoCep:
                var cep = Domain.Comum.Cep.Normalizar(texto);
                if (!Domain.Comum.Cep.EhValido(cep))
                {
                    t.Dizer("CEP inválido. Digite os 8 números (ex.: 13270000).");
                    return;
                }
                var local = await _cep.ConsultarAsync(cep, ct);
                if (local is null)
                {
                    t.Dizer("Não encontrei esse CEP. Confira e digite de novo.");
                    return;
                }
                e.Cep = cep;
                e.Logradouro = Texto.Limpar(local.Logradouro);
                e.Bairro = Texto.Limpar(local.Bairro);
                e.Cidade = local.Cidade;
                e.Uf = local.Uf;
                t.Etapa(EtapaConversa.InformandoNumero);
                t.Dizer(string.IsNullOrEmpty(e.Logradouro)
                    ? $"📍 {local.Cidade}/{local.Uf}. Esse CEP é geral da cidade: escreva *rua, número e bairro* (ex.: Rua das Flores, 120, Centro)."
                    : $"📍 {e.Logradouro}{(string.IsNullOrEmpty(e.Bairro) ? "" : " — " + e.Bairro)}, {local.Cidade}/{local.Uf}\nQual é o *número*?");
                return;

            case EtapaConversa.InformandoNumero:
                if (string.IsNullOrEmpty(e.Logradouro))
                {
                    var partes = texto.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (partes.Length < 3)
                    {
                        t.Dizer("Escreva separado por vírgulas: rua, número, bairro.");
                        return;
                    }
                    e.Logradouro = Cortar(partes[0], 200);
                    e.Numero = Cortar(partes[1], 20);
                    e.Bairro = Cortar(string.Join(", ", partes.Skip(2)), 100);
                }
                else
                {
                    var numero = Texto.Limpar(texto);
                    if (numero is null || numero.Length > 20)
                    {
                        t.Dizer("Informe o número (ou S/N).");
                        return;
                    }
                    e.Numero = numero;
                }
                if (string.IsNullOrEmpty(e.Bairro))
                {
                    e.Bairro = "Centro";
                }
                t.Etapa(EtapaConversa.InformandoComplemento);
                t.Dizer("Tem *complemento*? (apto, bloco, casa 2…) Se não tiver, toque em Pular.", new[] { new OpcaoMenu("pular", "Pular") });
                return;

            case EtapaConversa.InformandoComplemento:
                var complemento = pular || PalavrasPular.Contains(Normalizar(texto)) ? null : Cortar(Texto.Limpar(texto) ?? "", 100);
                await SalvarEnderecoAsync(t, string.IsNullOrEmpty(complemento) ? null : complemento, ct);
                return;
        }
    }

    private async Task<bool> CriarClienteAsync(Turno t, string? email, CancellationToken ct)
    {
        var e = t.Estado;
        if (e.Nome is null || e.Cpf is null)
        {
            await FinalizarAsync(t, ct);
            return false;
        }
        var telefone = TelefoneWhatsApp.ParaCadastro(t.Conversa.Telefone);
        var emailFinal = email;
        if (emailFinal is not null && await _db.Usuarios.AnyAsync(u => u.Email == emailFinal, ct))
        {
            t.Dizer("Esse e-mail já está cadastrado na loja, então os avisos deste pedido vão chegar por aqui mesmo. 👍");
            emailFinal = null;
        }
        emailFinal ??= EmailInterno(t.Conversa.Telefone);

        var agora = _relogio.AgoraUtc;
        var usuario = new Usuario
        {
            Nome = e.Nome,
            Email = emailFinal,
            EmailConfirmado = false,
            Cpf = e.Cpf,
            Telefone = telefone,
            Perfil = PerfilUsuario.Cliente,
            SenhaHash = ContaService.MarcadorContaWhatsApp,
            AceitouTermosEm = agora,
            CriadoEm = agora
        };
        _db.Usuarios.Add(usuario);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _db.Usuarios.Remove(usuario);
            _logger.LogWarning(ex, "Não foi possível criar o cliente da conversa {ConversaId}", t.Conversa.Id);
            t.Etapa(EtapaConversa.Menu);
            t.Dizer("Não consegui concluir o cadastro (CPF ou e-mail já cadastrados). Fale com um atendente, por favor.",
                new[] { new OpcaoMenu("menu:atendente", "Falar com atendente"), new OpcaoMenu("menu", "Menu") });
            return false;
        }

        t.Conversa.UsuarioId = usuario.Id;
        await _carrinhos.MesclarAoEntrarAsync(e.CarrinhoToken, usuario.Id, ct);
        e.CarrinhoToken = null;
        e.Nome = e.Cpf = null;
        _logger.LogInformation("Cliente {UsuarioId} cadastrado pelo WhatsApp (conversa {ConversaId})", usuario.Id, t.Conversa.Id);
        return true;
    }

    private async Task SalvarEnderecoAsync(Turno t, string? complemento, CancellationToken ct)
    {
        var e = t.Estado;
        if (t.UsuarioId is null || e.Cep is null || e.Logradouro is null || e.Numero is null)
        {
            await FinalizarAsync(t, ct);
            return;
        }
        var usuario = await _db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == t.UsuarioId, ct);
        var r = await _enderecos.SalvarAsync(t.UsuarioId.Value, new Endereco
        {
            Apelido = "WhatsApp",
            Destinatario = usuario.Nome,
            Cep = e.Cep,
            Logradouro = e.Logradouro,
            Numero = e.Numero,
            Complemento = complemento,
            Bairro = e.Bairro ?? "",
            Cidade = e.Cidade ?? "",
            Uf = e.Uf ?? ""
        }, ct);
        if (!r.Sucesso)
        {
            t.Dizer("Não consegui salvar o endereço: " + r.Erro + "\nVamos tentar de novo.");
            PedirCep(t);
            return;
        }
        e.EnderecoId = r.Valor;
        e.Cep = e.Logradouro = e.Bairro = e.Cidade = e.Uf = e.Numero = null;
        await CotarFreteAsync(t, ct);
    }

    // ===================================================================== texto livre (IA ou busca)

    private async Task TextoLivreAsync(Turno t, CancellationToken ct)
    {
        var texto = Cortar(t.Texto, 500);
        var interpretacao = await _assistente.InterpretarAsync(texto, await MontarContextoIaAsync(ct), ct);

        if (interpretacao is not null)
        {
            switch (interpretacao.Intencao)
            {
                case IntencaoCliente.Saudacao:
                case IntencaoCliente.Menu:
                    await MostrarMenuAsync(t, ct);
                    return;
                case IntencaoCliente.Atendente:
                    await ChamarAtendenteAsync(t, ct);
                    return;
                case IntencaoCliente.MeusPedidos:
                    await MostrarPedidosAsync(t, ct);
                    return;
                case IntencaoCliente.Carrinho:
                    await MostrarCarrinhoAsync(t, ct);
                    return;
                case IntencaoCliente.Duvida when !string.IsNullOrWhiteSpace(interpretacao.Resposta):
                    t.Etapa(EtapaConversa.Menu);
                    t.Dizer(Cortar(interpretacao.Resposta.Trim(), 700), new[]
                    {
                        new OpcaoMenu("menu:produtos", "Ver produtos"),
                        new OpcaoMenu("menu:atendente", "Falar com atendente"),
                        new OpcaoMenu("menu", "Menu")
                    });
                    return;
                case IntencaoCliente.Buscar:
                    await MostrarProdutosAsync(t, Cortar(string.IsNullOrWhiteSpace(interpretacao.Busca) ? texto : interpretacao.Busca.Trim(), 100), null, ct);
                    return;
            }
        }

        // Sem IA (ou ela não soube): tenta a busca do catálogo com o próprio texto
        if (texto.Length >= 3)
        {
            var pagina = await _catalogo.ListarVitrineAsync(new FiltroVitrine(Cortar(texto, 100), TamanhoPagina: 1), ct);
            if (pagina.TotalItens > 0)
            {
                await MostrarProdutosAsync(t, Cortar(texto, 100), null, ct);
                return;
            }
        }
        await MostrarMenuAsync(t, ct, "Não entendi muito bem. 🙂 Escolha uma opção abaixo ou escreva o nome do produto que procura.");
    }

    private async Task<ContextoLojaIA> MontarContextoIaAsync(CancellationToken ct)
    {
        var loja = await _identidade.ObterOuPadraoAsync(ct);
        var cfg = await _configuracaoLoja.ObterAsync(ct);
        var categorias = (await _catalogo.ListarCategoriasAsync(ct)).Select(c => c.Nome).ToList();
        var info = new StringBuilder()
            .Append("Formas de pagamento: Pix (aprovação na hora), cartão de crédito em até ").Append(cfg.ParcelasMaximas).Append("x e boleto.\n")
            .Append("Frete: calculado pelo CEP com as transportadoras; o prazo começa depois do pagamento confirmado")
            .Append(cfg.DiasManuseio > 0 ? $" (+{cfg.DiasManuseio} dia(s) para separação)" : "").Append(".\n");
        if (cfg.TemFreteGratis) info.Append("Frete grátis em compras acima de ").Append(Moeda(cfg.FreteGratisAPartirDe)).Append(".\n");
        info.Append("Cancelamento: pelo site ou com o atendente enquanto o pedido não foi enviado.\n");
        if (!string.IsNullOrEmpty(loja.Horario)) info.Append("Horário de atendimento: ").Append(loja.Horario).Append(".\n");
        if (!string.IsNullOrEmpty(loja.Endereco)) info.Append("Endereço: ").Append(loja.Endereco).Append(".\n");
        if (!string.IsNullOrEmpty(loja.Email)) info.Append("E-mail: ").Append(loja.Email).Append(".\n");
        info.Append("Site: ").Append(_links.Inicio());
        return new ContextoLojaIA(loja.NomeLoja, info.ToString(), categorias);
    }

    // ===================================================================== apoio

    /// <summary>
    /// Vincula a conversa a uma conta criada antes pelo WhatsApp com o mesmo número.
    /// Contas do site (com senha) nunca são vinculadas automaticamente: o número não é verificado no cadastro do site.
    /// </summary>
    private async Task VincularClienteAsync(Turno t, CancellationToken ct)
    {
        if (t.Conversa.UsuarioId is not null) return;
        var telefone = TelefoneWhatsApp.ParaCadastro(t.Conversa.Telefone);
        if (telefone is null) return;
        var candidatos = await _db.Usuarios.AsNoTracking()
            .Where(u => u.Telefone == telefone && u.SenhaHash == ContaService.MarcadorContaWhatsApp && !u.Anonimizado)
            .Select(u => u.Id)
            .Take(2)
            .ToListAsync(ct);
        if (candidatos.Count != 1) return;
        t.Conversa.UsuarioId = candidatos[0];
        await _carrinhos.MesclarAoEntrarAsync(t.Estado.CarrinhoToken, candidatos[0], ct);
        t.Estado.CarrinhoToken = null;
    }

    /// <summary>E-mail técnico das contas criadas sem e-mail: nada é enviado para ele.</summary>
    public string EmailInterno(string telefoneWhatsApp)
    {
        var dominio = _licenca.Estado.Ativa ? _licenca.Estado.Dados!.Dominios.FirstOrDefault(d => !d.StartsWith('*')) : null;
        return $"whatsapp+{Domain.Comum.Texto.SomenteDigitos(telefoneWhatsApp)}@{dominio ?? "whatsapp.invalid"}";
    }

    public static bool EhEmailInterno(string? email) =>
        email is not null && email.StartsWith("whatsapp+", StringComparison.OrdinalIgnoreCase);

    private static string Normalizar(string texto)
    {
        var t = texto.Trim().ToLowerInvariant().TrimEnd('!', '?', '.', ',');
        var semAcento = new StringBuilder();
        foreach (var c in t.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) semAcento.Append(c);
        return semAcento.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string? PrimeiroNome(string? nome)
    {
        var p = nome?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(p) || p.Length > 30 ? null : p;
    }

    private static string Moeda(decimal valor) => valor.ToString("C", PtBr);

    private static string Prazo(int dias) => dias == 1 ? "1 dia útil" : $"{dias} dias úteis";

    private static string Cortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";
}
