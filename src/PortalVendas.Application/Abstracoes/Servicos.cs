namespace PortalVendas.Application.Abstracoes;

/// <summary>Relógio abstrato para os testes controlarem o tempo.</summary>
public interface IRelogio
{
    DateTime AgoraUtc { get; }
}

public sealed class RelogioSistema : IRelogio
{
    public DateTime AgoraUtc => DateTime.UtcNow;
}

public interface IEmailSender
{
    /// <summary>Envia sem lançar exceção: falhas são registradas em log (e-mail não pode derrubar uma compra).</summary>
    Task EnviarAsync(string para, string assunto, string html, CancellationToken ct = default);
}

public record EnderecoCep(string Cep, string Logradouro, string Bairro, string Cidade, string Uf);

public interface ICepService
{
    /// <summary>Null quando o CEP não existe ou o serviço não respondeu.</summary>
    Task<EnderecoCep?> ConsultarAsync(string cep, CancellationToken ct = default);
}

public interface IArmazenamentoImagens
{
    /// <summary>Grava o conteúdo já validado e devolve o nome do arquivo gerado.</summary>
    Task<string> SalvarAsync(byte[] conteudo, string extensao, CancellationToken ct = default);
    Task RemoverAsync(string arquivo, CancellationToken ct = default);
}

public enum VerificacaoSenha
{
    Falha,
    Sucesso,
    SucessoPrecisaRehash
}

public interface IHashSenha
{
    string Gerar(string senha);
    VerificacaoSenha Verificar(string hash, string senha);
}

/// <summary>Cifra dados sensíveis gravados no banco (segredo TOTP).</summary>
public interface IProtetorDados
{
    string Proteger(string texto);
    string? Desproteger(string textoProtegido);
}

/// <summary>Links absolutos usados nos e-mails, montados a partir da URL configurada (nunca do cabeçalho Host).</summary>
public interface ILinksAplicacao
{
    string ConfirmarEmail(string token);
    string RedefinirSenha(string token);
    string Pedido(int pedidoId);
    string Inicio();
}
