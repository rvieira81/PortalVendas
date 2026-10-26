using System.Text;

namespace PortalVendas.Application.WhatsApp;

public enum ProvedorWhatsApp : byte
{
    Desativado = 0,
    /// <summary>WhatsApp Cloud API (oficial da Meta).</summary>
    Meta = 1,
    /// <summary>Evolution API (não oficial, lê o QR Code do aparelho da loja).</summary>
    Evolution = 2,
    /// <summary>Testes sem WhatsApp: as respostas do robô só ficam gravadas e aparecem no simulador do painel.</summary>
    Simulador = 3
}

/// <summary>Uma opção de menu. O cliente escolhe tocando no botão/lista ou digitando o número.</summary>
public sealed record OpcaoMenu(string Id, string Titulo, string? Descricao = null);

/// <summary>
/// Mensagem que o robô quer enviar, independente do provedor. O conector decide o formato:
/// na Meta vira botões (até 3) ou lista (até 10); na Evolution, texto com as opções numeradas.
/// </summary>
public sealed record MensagemSaida(string Texto, IReadOnlyList<OpcaoMenu>? Opcoes = null, string? ImagemUrl = null, string? TituloLista = null)
{
    public static MensagemSaida Simples(string texto) => new(texto);

    /// <summary>Texto com as opções numeradas ("1. Ver produtos"), usado na Evolution e no histórico do painel.</summary>
    public string TextoComOpcoes()
    {
        if (Opcoes is not { Count: > 0 }) return Texto;
        var sb = new StringBuilder(Texto.TrimEnd()).Append("\n");
        for (var i = 0; i < Opcoes.Count; i++)
        {
            sb.Append('\n').Append(i + 1).Append(". ").Append(Opcoes[i].Titulo);
            if (!string.IsNullOrEmpty(Opcoes[i].Descricao)) sb.Append(" — ").Append(Opcoes[i].Descricao);
        }
        sb.Append("\n\n_Responda com o número da opção._");
        return sb.ToString();
    }
}

/// <summary>Mensagem recebida pelo webhook, já traduzida do formato do provedor.</summary>
public sealed record MensagemRecebida(string Telefone, string? Nome, string Texto, string? IdOpcao, string IdExterno, DateTime RecebidaEmUtc);

public sealed record ResultadoEnvio(bool Sucesso, string? IdExterno, string? Erro)
{
    public static ResultadoEnvio Ok(string? id) => new(true, id, null);
    public static ResultadoEnvio Falha(string erro) => new(false, null, erro);
}

/// <summary>Envio de mensagens por um provedor (Meta ou Evolution).</summary>
public interface IConectorWhatsApp
{
    ProvedorWhatsApp Provedor { get; }

    Task<ResultadoEnvio> EnviarAsync(string telefone, MensagemSaida mensagem, CancellationToken ct = default);

    /// <summary>
    /// Mensagem de modelo aprovado (Meta) para avisar o cliente fora da janela de 24 h.
    /// Provedores sem essa regra mandam o texto normal.
    /// </summary>
    Task<ResultadoEnvio> EnviarModeloAsync(string telefone, string modelo, string idioma, IReadOnlyList<string> parametros,
        string textoAlternativo, CancellationToken ct = default);
}

/// <summary>Cria o conector conforme a configuração salva no painel. Null quando o WhatsApp está desligado.</summary>
public interface IFabricaConectorWhatsApp
{
    Task<IConectorWhatsApp?> CriarAsync(CancellationToken ct = default);
}

/// <summary>Fila em memória: o webhook grava a mensagem e responde na hora; o robô processa em segundo plano.</summary>
public interface IFilaWhatsApp
{
    void Enfileirar(long mensagemId);
}

// ------------------------------------------------------------------ IA

public enum IntencaoCliente
{
    Outro = 0,
    Buscar,
    Duvida,
    Atendente,
    MeusPedidos,
    Carrinho,
    Menu,
    Saudacao
}

/// <summary>O que a IA entendeu da mensagem livre do cliente.</summary>
public sealed record InterpretacaoIA(IntencaoCliente Intencao, string? Busca, string? Resposta);

/// <summary>Informações da loja que a IA pode usar para responder dúvidas (nada além disso).</summary>
public sealed record ContextoLojaIA(string NomeLoja, string Informacoes, IReadOnlyList<string> Categorias);

public interface IAssistenteVendas
{
    /// <summary>Null quando a IA está desligada, sem chave ou não respondeu: o robô cai na busca por palavras.</summary>
    Task<InterpretacaoIA?> InterpretarAsync(string mensagem, ContextoLojaIA contexto, CancellationToken ct = default);
}

/// <summary>Assistente nulo (IA desligada).</summary>
public sealed class SemAssistente : IAssistenteVendas
{
    public Task<InterpretacaoIA?> InterpretarAsync(string mensagem, ContextoLojaIA contexto, CancellationToken ct = default) =>
        Task.FromResult<InterpretacaoIA?>(null);
}

/// <summary>Avisa o cliente no WhatsApp quando o pedido muda de status (chamado pelo NotificadorPedidos).</summary>
public interface IAvisoPedidoWhatsApp
{
    Task AvisarAsync(int pedidoId, CancellationToken ct = default);
}

public static class TelefoneWhatsApp
{
    /// <summary>Só dígitos com DDI. Número brasileiro sem DDI (10 ou 11 dígitos) ganha o 55.</summary>
    public static string Normalizar(string? telefone)
    {
        var d = Domain.Comum.Texto.SomenteDigitos(telefone);
        if (d.Length is 10 or 11) d = "55" + d;
        return d.Length > 20 ? d[..20] : d;
    }

    /// <summary>Formato do cadastro (DDD + número, 10 ou 11 dígitos) a partir do número do WhatsApp.</summary>
    public static string? ParaCadastro(string telefoneWhatsApp)
    {
        var d = Domain.Comum.Texto.SomenteDigitos(telefoneWhatsApp);
        if (d.StartsWith("55", StringComparison.Ordinal) && d.Length is 12 or 13) d = d[2..];
        return d.Length is 10 or 11 ? d : null;
    }

    /// <summary>"+55 (19) 99999-0000" para exibir no painel.</summary>
    public static string Formatar(string telefone)
    {
        var cadastro = ParaCadastro(telefone);
        return cadastro is null ? "+" + telefone : "+55 " + Domain.Comum.Telefone.Formatar(cadastro);
    }
}
