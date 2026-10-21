using System.Text.Json;
using PortalVendas.Application.Frete;
using PortalVendas.Infrastructure.Frete;
using PortalVendas.Infrastructure.Pagamentos;

namespace PortalVendas.Tests.Infraestrutura;

public class MelhorEnvioTests
{
    [Fact]
    public void Le_cotacoes_e_ignora_servicos_com_erro()   // RF08
    {
        const string json = """
        [
          { "id": 1, "name": "PAC", "price": "25.10", "custom_price": "23.05", "delivery_time": 6, "custom_delivery_time": 7,
            "company": { "id": 1, "name": "Correios" } },
          { "id": 2, "name": "SEDEX", "price": 40.5, "custom_price": 38.9, "delivery_time": 2, "custom_delivery_time": 3,
            "company": { "id": 1, "name": "Correios" } },
          { "id": 3, "name": ".Package", "error": "Transportadora não atende este trecho.", "company": { "name": "Jadlog" } }
        ]
        """;

        var cotacoes = MelhorEnvioProvider.Ler(json);

        Assert.Equal(2, cotacoes.Count);
        var pac = cotacoes.Single(c => c.ServicoId == 1);
        Assert.Equal(23.05m, pac.Valor);         // usa custom_price (regras da conta)
        Assert.Equal(7, pac.PrazoDias);          // e custom_delivery_time
        Assert.Equal("Correios", pac.Transportadora);
        Assert.Equal(38.9m, cotacoes.Single(c => c.ServicoId == 2).Valor);
    }

    [Fact]
    public void Monta_corpo_com_peso_medidas_e_valor_declarado()
    {
        var corpo = MelhorEnvioProvider.MontarCorpo(
            new SolicitacaoFrete("13270000", "13084000", new[] { new ItemFrete(7, 0.35m, 8, 18, 20, 199.9m, 2) }),
            new MelhorEnvioOptions { Servicos = "1, 2" });

        using var doc = JsonDocument.Parse(corpo.ToJsonString());
        var r = doc.RootElement;
        Assert.Equal("13270000", r.GetProperty("from").GetProperty("postal_code").GetString());
        Assert.Equal("13084000", r.GetProperty("to").GetProperty("postal_code").GetString());
        var p = r.GetProperty("products")[0];
        Assert.Equal(0.35m, p.GetProperty("weight").GetDecimal());
        Assert.Equal(18m, p.GetProperty("width").GetDecimal());
        Assert.Equal(199.9m, p.GetProperty("insurance_value").GetDecimal());
        Assert.Equal(2, p.GetProperty("quantity").GetInt32());
        Assert.Equal("1,2", r.GetProperty("services").GetString());
    }
}

public class MercadoPagoTests
{
    [Fact]
    public void Le_resposta_de_pix()
    {
        const string json = """
        {
          "id": 1325641123, "status": "pending", "status_detail": "pending_waiting_transfer",
          "external_reference": "42", "transaction_amount": 225.3, "payment_method_id": "pix",
          "date_of_expiration": "2026-10-20T12:30:00.000-03:00",
          "point_of_interaction": { "transaction_data": { "qr_code": "00020126...", "qr_code_base64": "iVBORw0KGgo=" } }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var r = MercadoPagoGateway.Ler(doc.RootElement);

        Assert.Equal(1325641123L, r.Id);
        Assert.Equal("pending", r.Status);
        Assert.Equal("42", r.ReferenciaExterna);
        Assert.Equal(225.3m, r.Valor);
        Assert.Equal("00020126...", r.PixQrCode);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero), r.Expiracao!.Value.ToUniversalTime());
    }

    [Fact]
    public void Le_resposta_de_boleto_e_cartao()
    {
        const string boleto = """
        { "id": 1, "status": "pending", "transaction_amount": 10, "barcode": { "content": "23791234500000010009" },
          "transaction_details": { "external_resource_url": "https://www.mercadopago.com.br/payments/1/ticket" } }
        """;
        using (var doc = JsonDocument.Parse(boleto))
        {
            var r = MercadoPagoGateway.Ler(doc.RootElement);
            Assert.Equal("https://www.mercadopago.com.br/payments/1/ticket", r.BoletoUrl);
            Assert.Equal("23791234500000010009", r.BoletoLinhaDigitavel);
        }

        const string cartao = """
        { "id": 2, "status": "rejected", "status_detail": "cc_rejected_insufficient_amount", "transaction_amount": 10,
          "installments": 3, "card": { "last_four_digits": "4321" } }
        """;
        using (var doc = JsonDocument.Parse(cartao))
        {
            var r = MercadoPagoGateway.Ler(doc.RootElement);
            Assert.Equal("rejected", r.Status);
            Assert.Equal(3, r.Parcelas);
            Assert.Equal("4321", r.UltimosDigitos);
        }
    }
}
