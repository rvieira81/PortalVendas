namespace PortalVendas.Domain.WhatsApp;

/// <summary>Em que ponto do atendimento automático o cliente está.</summary>
public enum EtapaConversa : byte
{
    Menu = 1,
    EscolhendoCategoria = 2,
    EscolhendoProduto = 3,
    VendoProduto = 4,
    InformandoQuantidade = 5,
    Carrinho = 6,
    Buscando = 7,
    InformandoNome = 8,
    InformandoCpf = 9,
    InformandoEmail = 10,
    EscolhendoEndereco = 11,
    InformandoCep = 12,
    InformandoNumero = 13,
    InformandoComplemento = 14,
    EscolhendoFrete = 15,
    ConfirmandoPedido = 16,
    AguardandoPagamento = 17,
    MeusPedidos = 18
}

/// <summary>Uma conversa por número de WhatsApp. Guarda o estado do robô e o vínculo com o cliente.</summary>
public class ConversaWhatsApp
{
    /// <summary>Sem mensagem do cliente por este tempo, o robô recomeça pelo menu.</summary>
    public static readonly TimeSpan ExpiracaoContexto = TimeSpan.FromHours(6);

    /// <summary>Regra da Meta: mensagens livres só até 24 h depois da última mensagem do cliente.</summary>
    public static readonly TimeSpan JanelaAtendimento = TimeSpan.FromHours(24);

    public int Id { get; set; }
    public string Telefone { get; set; } = string.Empty;
    public string? NomeContato { get; set; }
    public int? UsuarioId { get; set; }
    public EtapaConversa Etapa { get; set; } = EtapaConversa.Menu;
    public string? Contexto { get; set; }
    public bool AtendimentoHumano { get; set; }
    public int NaoLidas { get; set; }
    public DateTime? UltimaEntradaEm { get; set; }
    public DateTime UltimaMensagemEm { get; set; }
    public DateTime CriadoEm { get; set; }

    public List<MensagemWhatsApp> Mensagens { get; set; } = new();

    public bool DentroDaJanela(DateTime agoraUtc) =>
        UltimaEntradaEm is not null && agoraUtc - UltimaEntradaEm.Value < JanelaAtendimento;
}

public class MensagemWhatsApp
{
    public long Id { get; set; }
    public int ConversaId { get; set; }
    public bool Entrada { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string? IdExterno { get; set; }
    public string? Autor { get; set; }
    public bool Processada { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; }
}
