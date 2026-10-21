namespace PortalVendas.Application.Pagamentos;

public record EnderecoPagador(string Cep, string Logradouro, string Numero, string Bairro, string Cidade, string Uf);

public record Pagador(string Email, string Nome, string Sobrenome, string Cpf, EnderecoPagador? Endereco = null);

public record SolicitacaoPix(Guid ChaveIdempotencia, int PedidoId, decimal Valor, string Descricao, Pagador Pagador, DateTimeOffset Expiracao);

public record SolicitacaoBoleto(Guid ChaveIdempotencia, int PedidoId, decimal Valor, string Descricao, Pagador Pagador, DateTimeOffset Vencimento);

public record SolicitacaoCartao(
    Guid ChaveIdempotencia, int PedidoId, decimal Valor, string Descricao,
    string Token, string MetodoPagamentoId, string? EmissorId, int Parcelas,
    string EmailPagador, string? TipoDocumento, string? NumeroDocumento);

public record RespostaGateway(
    long Id,
    string Status,
    string? StatusDetalhe,
    string? ReferenciaExterna,
    decimal Valor,
    string? MetodoPagamentoId,
    string? PixQrCode = null,
    string? PixQrCodeBase64 = null,
    string? BoletoUrl = null,
    string? BoletoLinhaDigitavel = null,
    DateTimeOffset? Expiracao = null,
    int? Parcelas = null,
    string? UltimosDigitos = null);

/// <summary>Erro de comunicação ou recusa técnica da API do gateway (não confundir com cartão recusado).</summary>
public sealed class GatewayPagamentoException : Exception
{
    public GatewayPagamentoException(string mensagem, Exception? interna = null) : base(mensagem, interna) { }
}

public interface IGatewayPagamento
{
    Task<RespostaGateway> CriarPixAsync(SolicitacaoPix solicitacao, CancellationToken ct = default);
    Task<RespostaGateway> CriarBoletoAsync(SolicitacaoBoleto solicitacao, CancellationToken ct = default);
    Task<RespostaGateway> CriarCartaoAsync(SolicitacaoCartao solicitacao, CancellationToken ct = default);
    Task<RespostaGateway> ConsultarAsync(long gatewayId, CancellationToken ct = default);
    /// <summary>Estorno total. Idempotente pela chave.</summary>
    Task EstornarAsync(long gatewayId, Guid chaveIdempotencia, CancellationToken ct = default);
}
