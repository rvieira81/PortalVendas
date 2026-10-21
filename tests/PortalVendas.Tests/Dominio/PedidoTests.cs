using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Pedidos;

namespace PortalVendas.Tests.Dominio;

public class PedidoTests
{
    private static readonly DateTime Agora = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Pedido NovoPedido(decimal frete = 20m, decimal desconto = 0m) => Pedido.Criar(
        usuarioId: 1,
        chaveIdempotencia: Guid.NewGuid(),
        itens: new[] { (1, "Fone", "SKU-1", 199.90m, 2), (2, "Cabo", "SKU-2", 29.90m, 1) },
        entrega: new EnderecoEntrega("Maria", "13084000", "Rua A", "10", null, "Centro", "Campinas", "SP"),
        freteServicoId: 1,
        freteDescricao: "Correios — PAC",
        valorFrete: frete,
        fretePrazoDias: 6,
        desconto: desconto,
        agoraUtc: Agora,
        autor: "teste");

    [Fact]
    public void Total_e_calculado_no_dominio()   // RN05
    {
        var p = NovoPedido(frete: 20m, desconto: 10m);
        Assert.Equal(429.70m, p.Subtotal);
        Assert.Equal(439.70m, p.Total);
        Assert.Equal(StatusPedido.AguardandoPagamento, p.Status);
        Assert.Single(p.Historico);
        Assert.Equal(Agora.Add(Pedido.PrazoParaIniciarPagamento), p.ExpiraEm);
    }

    [Fact]
    public void Desconto_nunca_deixa_total_negativo()
    {
        var p = NovoPedido(frete: 0m, desconto: 10_000m);
        Assert.Equal(0m, p.Total);
    }

    [Fact]
    public void Fluxo_completo_de_status()   // RN07
    {
        var p = NovoPedido();
        p.ConfirmarPagamento(Agora, "mp", "Pix aprovado");
        p.IniciarSeparacao(Agora, "admin");
        p.MarcarEnviado("aa123456789br", Agora, "admin");
        p.MarcarEntregue(Agora, "admin");

        Assert.Equal(StatusPedido.Entregue, p.Status);
        Assert.Equal("AA123456789BR", p.CodigoRastreio);
        Assert.Equal(5, p.Historico.Count);
        Assert.Equal(4, p.Versao);
    }

    [Fact]
    public void Nao_envia_pedido_sem_pagamento()   // RF18
    {
        var p = NovoPedido();
        Assert.Throws<DomainException>(() => p.MarcarEnviado("AA1", Agora, "admin"));
        Assert.Throws<DomainException>(() => p.IniciarSeparacao(Agora, "admin"));
    }

    [Fact]
    public void Envio_exige_codigo_de_rastreio()
    {
        var p = NovoPedido();
        p.ConfirmarPagamento(Agora, "mp", "ok");
        p.IniciarSeparacao(Agora, "admin");
        Assert.Throws<DomainException>(() => p.MarcarEnviado("  ", Agora, "admin"));
    }

    [Fact]
    public void Pedido_enviado_nao_pode_ser_cancelado()   // RF16
    {
        var p = NovoPedido();
        p.ConfirmarPagamento(Agora, "mp", "ok");
        p.IniciarSeparacao(Agora, "admin");
        p.MarcarEnviado("AA1", Agora, "admin");
        Assert.False(p.PodeSerCanceladoPeloCliente);
        Assert.Throws<DomainException>(() => p.Cancelar("desisti", Agora, "cliente"));
    }

    [Fact]
    public void Analise_reprovada_volta_a_aguardar_pagamento()
    {
        var p = NovoPedido();
        p.ColocarEmAnalise(Agora, "mp");
        p.VoltarParaAguardandoPagamento(Agora, "mp", "recusado");
        Assert.True(p.PodeReceberPagamento);
    }

    [Theory]
    [InlineData(StatusPedido.Pago, StatusPedido.AguardandoPagamento)]
    [InlineData(StatusPedido.Entregue, StatusPedido.Enviado)]
    [InlineData(StatusPedido.Cancelado, StatusPedido.Pago)]
    [InlineData(StatusPedido.Enviado, StatusPedido.Cancelado)]
    public void Transicoes_proibidas(StatusPedido de, StatusPedido para) =>
        Assert.False(Pedido.TransicaoPermitida(de, para));
}
