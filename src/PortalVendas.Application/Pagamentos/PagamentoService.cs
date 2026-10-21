using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Emails;
using PortalVendas.Application.Pedidos;
using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Pagamentos;
using PortalVendas.Domain.Pedidos;
using PortalVendas.Domain.Usuarios;

namespace PortalVendas.Application.Pagamentos;

public record DadosCartao(
    string Token, string MetodoPagamentoId, string? EmissorId, int Parcelas,
    string? TipoDocumento, string? NumeroDocumento);

public record ResultadoCartao(bool Aprovado, bool EmAnalise, string Mensagem);

public record NotificacaoRecebida(string? RequestId, string Topico, string RecursoId, string? Corpo, bool AssinaturaValida);

public class PagamentoService
{
    /// <summary>RN04</summary>
    public static readonly TimeSpan ValidadePix = TimeSpan.FromMinutes(30);
    public const int DiasUteisVencimentoBoleto = 3;

    /// <summary>Folga antes de cancelar por expiração: o pagamento pode ter sido feito no limite do prazo.</summary>
    public static readonly TimeSpan ToleranciaPix = TimeSpan.FromMinutes(15);
    /// <summary>Boleto pago no vencimento pode levar até 3 dias úteis para compensar.</summary>
    public static readonly TimeSpan ToleranciaBoleto = TimeSpan.FromDays(5);

    private readonly IAppDbContext _db;
    private readonly IGatewayPagamento _gateway;
    private readonly EstoqueService _estoque;
    private readonly ConfiguracaoLojaService _configuracao;
    private readonly NotificadorPedidos _notificador;
    private readonly IRelogio _relogio;
    private readonly ILogger<PagamentoService> _logger;

    public PagamentoService(IAppDbContext db, IGatewayPagamento gateway, EstoqueService estoque, ConfiguracaoLojaService configuracao,
        NotificadorPedidos notificador, IRelogio relogio, ILogger<PagamentoService> logger)
    {
        _db = db;
        _gateway = gateway;
        _estoque = estoque;
        _configuracao = configuracao;
        _notificador = notificador;
        _relogio = relogio;
        _logger = logger;
    }

    // ================================================================== consulta

    public Task<Pedido?> ObterPedidoParaPagamentoAsync(int usuarioId, int pedidoId, CancellationToken ct = default) =>
        _db.Pedidos.AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .FirstOrDefaultAsync(p => p.Id == pedidoId && p.UsuarioId == usuarioId, ct);

    public async Task<int> ParcelasPermitidasAsync(decimal total, CancellationToken ct = default) =>
        (await _configuracao.ObterAsync(ct)).ParcelasPermitidas(total);

    // ================================================================== Pix (RF10)

    public async Task<Resultado<Pagamento>> GerarPixAsync(int usuarioId, int pedidoId, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;
        var (pedido, usuario, erro) = await CarregarParaNovoPagamentoAsync(usuarioId, pedidoId, ct);
        if (erro is not null) return Resultado<Pagamento>.Falha(erro);

        // Reaproveita um Pix ainda válido em vez de gerar outro
        var vigente = pedido!.Pagamentos
            .Where(p => p.Metodo == MetodoPagamento.Pix && p.PixOuBoletoAindaValido(agora))
            .OrderByDescending(p => p.CriadoEm)
            .FirstOrDefault();
        if (vigente is not null) return Resultado<Pagamento>.Ok(vigente);

        var pagamento = await RegistrarTentativaAsync(pedido, MetodoPagamento.Pix, agora, ct);
        var expiracao = agora.Add(ValidadePix);

        try
        {
            var resposta = await _gateway.CriarPixAsync(new SolicitacaoPix(
                pagamento.ChaveIdempotencia, pedido.Id, pedido.Total, DescricaoCobranca(pedido),
                MontarPagador(usuario!, null), Horario.ParaBrasiliaOffset(expiracao)), ct);

            await AplicarRespostaAsync(pagamento.Id, resposta, (pg, pd) =>
            {
                var expiraEm = resposta.Expiracao?.UtcDateTime ?? expiracao;
                pg.DefinirDadosPix(resposta.PixQrCode, resposta.PixQrCodeBase64, expiraEm);
                pd.DefinirExpiracao(expiraEm, agora);
            }, ct);

            return Resultado<Pagamento>.Ok(await _db.Pagamentos.AsNoTracking().FirstAsync(p => p.Id == pagamento.Id, ct));
        }
        catch (GatewayPagamentoException ex)
        {
            await RegistrarFalhaComunicacaoAsync(pagamento.Id, ex, ct);
            return Resultado<Pagamento>.Falha("Não foi possível gerar o Pix agora. Tente novamente em instantes ou escolha outra forma de pagamento.");
        }
    }

    // ================================================================== Boleto (RF12)

    public async Task<Resultado<Pagamento>> GerarBoletoAsync(int usuarioId, int pedidoId, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;
        var (pedido, usuario, erro) = await CarregarParaNovoPagamentoAsync(usuarioId, pedidoId, ct);
        if (erro is not null) return Resultado<Pagamento>.Falha(erro);

        var vigente = pedido!.Pagamentos
            .Where(p => p.Metodo == MetodoPagamento.Boleto && p.PixOuBoletoAindaValido(agora))
            .OrderByDescending(p => p.CriadoEm)
            .FirstOrDefault();
        if (vigente is not null) return Resultado<Pagamento>.Ok(vigente);

        // Vencimento: 3 dias úteis, até 23:59:59 no horário de Brasília
        var dataVencimento = DiasUteis.Somar(Horario.HojeEmBrasilia(agora), DiasUteisVencimentoBoleto);
        var vencimentoUtc = Horario.DeBrasiliaParaUtc(dataVencimento.ToDateTime(new TimeOnly(23, 59, 59)));

        var e = pedido.Entrega;
        var pagador = MontarPagador(usuario!, new EnderecoPagador(e.Cep, e.Logradouro, e.Numero, e.Bairro, e.Cidade, e.Uf));
        var pagamento = await RegistrarTentativaAsync(pedido, MetodoPagamento.Boleto, agora, ct);

        try
        {
            var resposta = await _gateway.CriarBoletoAsync(new SolicitacaoBoleto(
                pagamento.ChaveIdempotencia, pedido.Id, pedido.Total, DescricaoCobranca(pedido),
                pagador, Horario.ParaBrasiliaOffset(vencimentoUtc)), ct);

            await AplicarRespostaAsync(pagamento.Id, resposta, (pg, pd) =>
            {
                var expiraEm = resposta.Expiracao?.UtcDateTime ?? vencimentoUtc;
                pg.DefinirDadosBoleto(resposta.BoletoUrl, resposta.BoletoLinhaDigitavel, expiraEm);
                pd.DefinirExpiracao(expiraEm, agora);
            }, ct);

            return Resultado<Pagamento>.Ok(await _db.Pagamentos.AsNoTracking().FirstAsync(p => p.Id == pagamento.Id, ct));
        }
        catch (GatewayPagamentoException ex)
        {
            await RegistrarFalhaComunicacaoAsync(pagamento.Id, ex, ct);
            return Resultado<Pagamento>.Falha("Não foi possível gerar o boleto agora. Tente novamente em instantes ou escolha outra forma de pagamento.");
        }
    }

    // ================================================================== Cartão (RF11)

    public async Task<Resultado<ResultadoCartao>> PagarComCartaoAsync(int usuarioId, int pedidoId, DadosCartao dados, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dados.Token) || dados.Token.Length > 200)
            return Resultado<ResultadoCartao>.Falha("Dados do cartão inválidos. Preencha novamente.");
        if (string.IsNullOrWhiteSpace(dados.MetodoPagamentoId) || dados.MetodoPagamentoId.Length > 30)
            return Resultado<ResultadoCartao>.Falha("Bandeira do cartão não identificada.");

        var agora = _relogio.AgoraUtc;
        var (pedido, usuario, erro) = await CarregarParaNovoPagamentoAsync(usuarioId, pedidoId, ct);
        if (erro is not null) return Resultado<ResultadoCartao>.Falha(erro);

        // RN06: parcelas validadas no servidor; o valor é sempre o total do pedido
        var maximo = (await _configuracao.ObterAsync(ct)).ParcelasPermitidas(pedido!.Total);
        if (dados.Parcelas < 1 || dados.Parcelas > maximo)
            return Resultado<ResultadoCartao>.Falha($"Número de parcelas inválido. Máximo para este pedido: {maximo}x.");

        var pagamento = await RegistrarTentativaAsync(pedido, MetodoPagamento.CartaoCredito, agora, ct);

        RespostaGateway resposta;
        try
        {
            resposta = await _gateway.CriarCartaoAsync(new SolicitacaoCartao(
                pagamento.ChaveIdempotencia, pedido.Id, pedido.Total, DescricaoCobranca(pedido),
                dados.Token, dados.MetodoPagamentoId, dados.EmissorId, dados.Parcelas,
                usuario!.Email, dados.TipoDocumento, Texto.SomenteDigitos(dados.NumeroDocumento)), ct);
        }
        catch (GatewayPagamentoException ex)
        {
            await RegistrarFalhaComunicacaoAsync(pagamento.Id, ex, ct);
            return Resultado<ResultadoCartao>.Falha("Não conseguimos processar o cartão agora. Tente novamente ou escolha Pix ou boleto.");
        }

        await AplicarRespostaAsync(pagamento.Id, resposta, (pg, _) =>
            pg.DefinirDadosCartao(dados.Parcelas, resposta.MetodoPagamentoId ?? dados.MetodoPagamentoId, resposta.UltimosDigitos), ct);

        return Pagamento.ConverterStatus(resposta.Status) switch
        {
            StatusPagamento.Aprovado => Resultado<ResultadoCartao>.Ok(new ResultadoCartao(true, false, "Pagamento aprovado!")),
            StatusPagamento.EmAnalise or StatusPagamento.Pendente => Resultado<ResultadoCartao>.Ok(new ResultadoCartao(false, true,
                "Seu pagamento está em análise pela operadora. Você receberá um e-mail com a resposta.")),
            _ => Resultado<ResultadoCartao>.Falha(MensagemRecusa(resposta.StatusDetalhe))
        };
    }

    /// <summary>Motivos de recusa do Mercado Pago em linguagem simples (RF11).</summary>
    public static string MensagemRecusa(string? statusDetalhe) => statusDetalhe switch
    {
        "cc_rejected_insufficient_amount" => "Pagamento recusado por saldo ou limite insuficiente. Tente outro cartão ou Pix.",
        "cc_rejected_bad_filled_security_code" => "Código de segurança (CVV) incorreto. Confira e tente de novo.",
        "cc_rejected_bad_filled_date" => "Data de validade incorreta. Confira e tente de novo.",
        "cc_rejected_bad_filled_card_number" => "Número do cartão incorreto. Confira e tente de novo.",
        "cc_rejected_bad_filled_other" => "Algum dado do cartão está incorreto. Confira e tente de novo.",
        "cc_rejected_call_for_authorize" => "O banco pediu autorização para esta compra. Ligue para a central do cartão e tente de novo.",
        "cc_rejected_card_disabled" => "O cartão não está ativo. Ligue para o banco para ativá-lo ou use outro cartão.",
        "cc_rejected_duplicated_payment" => "Você já fez um pagamento com este valor. Se precisar pagar de novo, use outro cartão ou forma de pagamento.",
        "cc_rejected_high_risk" or "cc_rejected_blacklist" => "Pagamento recusado pela análise de segurança. Use outro cartão ou pague com Pix ou boleto.",
        "cc_rejected_max_attempts" => "Limite de tentativas atingido. Use outro cartão ou outra forma de pagamento.",
        "cc_rejected_invalid_installments" => "O cartão não aceita este número de parcelas. Escolha outra quantidade.",
        _ => "Pagamento recusado pelo emissor do cartão. Tente outro cartão ou pague com Pix ou boleto."
    };

    // ================================================================== Webhook e conciliação (RF13)

    public async Task ProcessarNotificacaoAsync(NotificacaoRecebida n, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;

        NotificacaoGateway? registro = null;
        if (!string.IsNullOrEmpty(n.RequestId))
        {
            registro = await _db.NotificacoesGateway.FirstOrDefaultAsync(x => x.RequestId == n.RequestId, ct);
            if (registro is { Processada: true }) return;   // reenvio de notificação já tratada
        }

        if (registro is null)
        {
            registro = new NotificacaoGateway
            {
                RequestId = Limitar(n.RequestId, 100),
                Topico = Limitar(n.Topico, 50) ?? "",
                RecursoId = Limitar(n.RecursoId, 50) ?? "",
                Corpo = n.Corpo is { Length: > 20000 } ? n.Corpo[..20000] : n.Corpo,
                AssinaturaValida = n.AssinaturaValida,
                RecebidaEm = agora
            };
            _db.NotificacoesGateway.Add(registro);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();   // mesma notificação chegou em paralelo
                return;
            }
        }

        if (!n.AssinaturaValida) return;

        var registroId = registro.Id;
        bool processada;
        string? erro = null;

        if (!string.Equals(n.Topico, "payment", StringComparison.OrdinalIgnoreCase))
        {
            processada = true;   // outros tópicos não interessam
        }
        else if (!long.TryParse(n.RecursoId, out var gatewayId))
        {
            processada = true;
            erro = "Id de recurso inválido";
        }
        else
        {
            try
            {
                processada = await SincronizarPagamentoAsync(gatewayId, ct);
                if (!processada) erro = "Pagamento não encontrado na base (pode ser de outra integração)";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                processada = false;
                erro = ex.Message;
                _logger.LogError(ex, "Erro ao processar notificação {RequestId} do pagamento {GatewayId}", n.RequestId, gatewayId);
            }
        }

        _db.ChangeTracker.Clear();
        var atual = await _db.NotificacoesGateway.FirstAsync(x => x.Id == registroId, ct);
        atual.Processada = processada;
        atual.Erro = Limitar(erro, 1000);
        atual.ProcessadaEm = _relogio.AgoraUtc;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Consulta o pagamento na API (fonte da verdade, RN08) e aplica no pedido.
    /// False se o pagamento não é desta loja.
    /// </summary>
    public async Task<bool> SincronizarPagamentoAsync(long gatewayId, CancellationToken ct = default)
    {
        var pagamentoId = await _db.Pagamentos.AsNoTracking()
            .Where(p => p.GatewayId == gatewayId)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (pagamentoId is null)
        {
            _logger.LogWarning("Notificação de pagamento {GatewayId} sem registro correspondente", gatewayId);
            return false;
        }

        var resposta = await _gateway.ConsultarAsync(gatewayId, ct);
        await AplicarRespostaAsync(pagamentoId.Value, resposta, null, ct);
        return true;
    }

    /// <summary>Job: reconsulta pagamentos pendentes ou em análise (caso um webhook tenha se perdido).</summary>
    public async Task<int> ConciliarPendentesAsync(CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;
        var limite = agora.AddMinutes(-10);
        var desde = agora.AddDays(-30);

        var ids = await _db.Pagamentos.AsNoTracking()
            .Where(p => (p.Status == StatusPagamento.Pendente || p.Status == StatusPagamento.EmAnalise)
                        && p.GatewayId != null && p.CriadoEm < limite && p.CriadoEm > desde)
            .OrderBy(p => p.CriadoEm)
            .Select(p => p.GatewayId!.Value)
            .Take(50)
            .ToListAsync(ct);

        var conciliados = 0;
        foreach (var id in ids)
        {
            try
            {
                await SincronizarPagamentoAsync(id, ct);
                conciliados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Falha ao conciliar o pagamento {GatewayId}", id);
            }
            _db.ChangeTracker.Clear();
        }
        return conciliados;
    }

    /// <summary>Job: cancela pedidos cujo prazo de pagamento passou (RN04) e libera a reserva.</summary>
    public async Task<int> ExpirarPedidosAsync(CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;
        var candidatos = await _db.Pedidos.AsNoTracking()
            .Where(p => p.Status == StatusPedido.AguardandoPagamento && p.ExpiraEm != null && p.ExpiraEm < agora)
            .OrderBy(p => p.ExpiraEm)
            .Select(p => p.Id)
            .Take(100)
            .ToListAsync(ct);

        var cancelados = 0;
        foreach (var pedidoId in candidatos)
        {
            try
            {
                if (await ExpirarPedidoAsync(pedidoId, ct)) cancelados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Falha ao expirar o pedido {PedidoId}", pedidoId);
            }
            _db.ChangeTracker.Clear();
        }
        return cancelados;
    }

    private async Task<bool> ExpirarPedidoAsync(int pedidoId, CancellationToken ct)
    {
        // Antes de cancelar, pergunta ao gateway: o pagamento pode ter sido aprovado sem o webhook chegar
        var pendentes = await _db.Pagamentos.AsNoTracking()
            .Where(p => p.PedidoId == pedidoId && p.GatewayId != null
                        && (p.Status == StatusPagamento.Pendente || p.Status == StatusPagamento.EmAnalise))
            .Select(p => p.GatewayId!.Value)
            .ToListAsync(ct);
        foreach (var gatewayId in pendentes)
        {
            await SincronizarPagamentoAsync(gatewayId, ct);   // se falhar, lança e o pedido fica para o próximo ciclo
            _db.ChangeTracker.Clear();
        }

        var agora = _relogio.AgoraUtc;
        var pedido = await _db.Pedidos.Include(p => p.Itens).Include(p => p.Pagamentos).FirstAsync(p => p.Id == pedidoId, ct);
        if (pedido.Status != StatusPedido.AguardandoPagamento || pedido.ExpiraEm is null) return false;

        var ultimo = pedido.Pagamentos.OrderByDescending(p => p.CriadoEm).FirstOrDefault();
        var tolerancia = ultimo is { Status: StatusPagamento.Pendente }
            ? (ultimo.Metodo == MetodoPagamento.Boleto ? ToleranciaBoleto : ToleranciaPix)
            : TimeSpan.Zero;
        if (agora < pedido.ExpiraEm.Value.Add(tolerancia)) return false;

        var estrategia = _db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _db.Database.BeginTransactionAsync(ct);
            await _estoque.LiberarReservaAsync(pedido, ct);
            pedido.Cancelar("Prazo de pagamento expirado", agora, Autores.Sistema);
            await _db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        });

        _logger.LogInformation("Pedido {PedidoId} cancelado por expiração do prazo de pagamento", pedidoId);
        await _notificador.NotificarStatusAsync(pedidoId, ct);
        return true;
    }

    // ================================================================== núcleo

    /// <summary>
    /// Aplica a resposta do gateway ao pagamento e, se o status mudou, ao pedido e ao estoque,
    /// tudo numa transação. Reexecuta em caso de conflito de concorrência (webhook × job × tela).
    /// </summary>
    private async Task AplicarRespostaAsync(int pagamentoId, RespostaGateway resposta, Action<Pagamento, Pedido>? complementar, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                var pedidoParaNotificar = await AplicarRespostaUmaVezAsync(pagamentoId, resposta, complementar, ct);
                if (pedidoParaNotificar is not null)
                    await _notificador.NotificarStatusAsync(pedidoParaNotificar.Value, ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (tentativa < 3)
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<int?> AplicarRespostaUmaVezAsync(int pagamentoId, RespostaGateway resposta, Action<Pagamento, Pedido>? complementar, CancellationToken ct)
    {
        var estrategia = _db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _db.Database.BeginTransactionAsync(ct);
            var agora = _relogio.AgoraUtc;

            var pedidoId = await _db.Pagamentos.Where(p => p.Id == pagamentoId).Select(p => p.PedidoId).FirstAsync(ct);
            var pedido = await _db.Pedidos.Include(p => p.Itens).Include(p => p.Pagamentos).FirstAsync(p => p.Id == pedidoId, ct);
            var pagamento = pedido.Pagamentos.First(p => p.Id == pagamentoId);
            var statusPedidoAntes = pedido.Status;

            var mudou = pagamento.AtualizarDoGateway(resposta.Id, resposta.Status, resposta.StatusDetalhe, agora);
            complementar?.Invoke(pagamento, pedido);

            if (mudou)
            {
                _logger.LogInformation("Pagamento {PagamentoId} (MP {GatewayId}) do pedido {PedidoId}: {Status} ({Detalhe})",
                    pagamento.Id, resposta.Id, pedido.Id, pagamento.Status, resposta.StatusDetalhe);
                await AplicarNoPedidoAsync(pedido, pagamento, agora, ct);
            }

            await _db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return pedido.Status != statusPedidoAntes ? pedido.Id : (int?)null;
        });
    }

    private async Task AplicarNoPedidoAsync(Pedido pedido, Pagamento pagamento, DateTime agora, CancellationToken ct)
    {
        switch (pagamento.Status)
        {
            case StatusPagamento.Aprovado:
                var outroAprovado = pedido.Pagamentos.Any(p => p.Id != pagamento.Id && p.Status == StatusPagamento.Aprovado);
                if ((pedido.Status is StatusPedido.AguardandoPagamento or StatusPedido.PagamentoEmAnalise) && !outroAprovado)
                {
                    await _estoque.BaixarReservaAsync(pedido, ct);
                    pedido.ConfirmarPagamento(agora, Autores.Gateway,
                        $"{pagamento.Metodo.Descricao()} aprovado (Mercado Pago {pagamento.GatewayId})");
                }
                else
                {
                    // Pago depois do cancelamento por prazo, ou pagamento em duplicidade (ex.: Pix antigo + cartão)
                    var motivo = pedido.Status == StatusPedido.Cancelado ? "pedido já cancelado" : "pedido já pago por outro meio";
                    await _gateway.EstornarAsync(pagamento.GatewayId!.Value, pagamento.ChaveIdempotencia, ct);
                    pagamento.MarcarEstornado(agora);
                    pedido.RegistrarObservacao(
                        $"{pagamento.Metodo.Descricao()} {pagamento.GatewayId} aprovado com {motivo}: estornado automaticamente.",
                        agora, Autores.Sistema);
                    _logger.LogWarning("Pagamento {GatewayId} estornado automaticamente: {Motivo} (pedido {PedidoId})",
                        pagamento.GatewayId, motivo, pedido.Id);
                }
                break;

            case StatusPagamento.EmAnalise:
                if (pedido.Status == StatusPedido.AguardandoPagamento)
                    pedido.ColocarEmAnalise(agora, Autores.Gateway);
                break;

            case StatusPagamento.Recusado:
            case StatusPagamento.Cancelado:
                var outroEmAnalise = pedido.Pagamentos.Any(p => p.Id != pagamento.Id && p.Status == StatusPagamento.EmAnalise);
                if (pedido.Status == StatusPedido.PagamentoEmAnalise && !outroEmAnalise)
                    pedido.VoltarParaAguardandoPagamento(agora, Autores.Gateway,
                        $"Pagamento não aprovado ({pagamento.StatusDetalhe ?? pagamento.StatusGateway})");
                break;

            case StatusPagamento.Estornado:
            case StatusPagamento.Contestado:
                if (pedido.EstoqueBaixado)
                {
                    pedido.RegistrarObservacao(
                        $"ATENÇÃO: pagamento {pagamento.GatewayId} ficou \"{pagamento.Status.Descricao()}\" no Mercado Pago. Verifique antes de enviar.",
                        agora, Autores.Gateway);
                    _logger.LogWarning("Pagamento {GatewayId} do pedido {PedidoId} ficou {Status}", pagamento.GatewayId, pedido.Id, pagamento.Status);
                }
                break;
        }
    }

    // ================================================================== auxiliares

    private async Task<(Pedido? Pedido, Usuario? Usuario, string? Erro)> CarregarParaNovoPagamentoAsync(int usuarioId, int pedidoId, CancellationToken ct)
    {
        var pedido = await _db.Pedidos
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .FirstOrDefaultAsync(p => p.Id == pedidoId && p.UsuarioId == usuarioId, ct);
        if (pedido is null) return (null, null, "Pedido não encontrado.");
        if (!pedido.PodeReceberPagamento) return (null, null, "Este pedido não está aguardando pagamento.");

        var usuario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null || string.IsNullOrEmpty(usuario.Cpf)) return (null, null, "Complete seu cadastro (CPF) para pagar.");
        return (pedido, usuario, null);
    }

    /// <summary>Grava a tentativa antes de chamar o gateway, para ter a chave de idempotência registrada.</summary>
    private async Task<Pagamento> RegistrarTentativaAsync(Pedido pedido, MetodoPagamento metodo, DateTime agora, CancellationToken ct)
    {
        var pagamento = new Pagamento(pedido.Id, metodo, pedido.Total, agora);
        _db.Pagamentos.Add(pagamento);
        await _db.SaveChangesAsync(ct);
        return pagamento;
    }

    private async Task RegistrarFalhaComunicacaoAsync(int pagamentoId, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Falha de comunicação com o gateway no pagamento {PagamentoId}", pagamentoId);
        _db.ChangeTracker.Clear();
        var pagamento = await _db.Pagamentos.FirstAsync(p => p.Id == pagamentoId, ct);
        pagamento.MarcarFalhaComunicacao("erro_comunicacao", _relogio.AgoraUtc);
        await _db.SaveChangesAsync(ct);
    }

    private static Pagador MontarPagador(Usuario usuario, EnderecoPagador? endereco)
    {
        var partes = usuario.Nome.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var nome = partes.Length > 0 ? partes[0] : usuario.Nome;
        var sobrenome = partes.Length > 1 ? partes[1] : nome;
        return new Pagador(usuario.Email, nome, sobrenome, usuario.Cpf ?? string.Empty, endereco);
    }

    private static string DescricaoCobranca(Pedido pedido) => $"Pedido {pedido.Numero}";

    private static string? Limitar(string? texto, int max) => texto is null ? null : (texto.Length > max ? texto[..max] : texto);
}
