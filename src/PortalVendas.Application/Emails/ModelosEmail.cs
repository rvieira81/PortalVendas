using System.Globalization;
using System.Net;
using PortalVendas.Domain.Pedidos;

namespace PortalVendas.Application.Emails;

public record MensagemEmail(string Assunto, string Html);

/// <summary>Modelos de e-mail. Todo dado variável passa por HtmlEncode.</summary>
public static class ModelosEmail
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static string E(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty);

    private static string Moldura(string titulo, string corpo) => $"""
        <!doctype html>
        <html lang="pt-BR"><body style="margin:0;padding:24px;background:#f4f5f7;font-family:Segoe UI,Arial,sans-serif;color:#1f2933">
        <table role="presentation" width="100%" style="max-width:560px;margin:0 auto;background:#fff;border-radius:8px;padding:24px">
        <tr><td>
        <h1 style="font-size:20px;margin:0 0 16px">{E(titulo)}</h1>
        {corpo}
        <p style="font-size:12px;color:#6b7280;margin-top:32px">Você recebeu este e-mail porque tem uma conta em nossa loja. Não responda: esta caixa não é monitorada.</p>
        </td></tr></table></body></html>
        """;

    private static string Botao(string url, string texto) =>
        $"""<p><a href="{E(url)}" style="display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;padding:10px 18px;border-radius:6px">{E(texto)}</a></p>""";

    public static MensagemEmail ConfirmacaoCadastro(string nome, string link) => new(
        "Confirme seu e-mail",
        Moldura("Bem-vindo(a)!", $"""
            <p>Olá, {E(nome)}.</p>
            <p>Para concluir o cadastro e poder finalizar compras, confirme seu e-mail:</p>
            {Botao(link, "Confirmar e-mail")}
            <p style="font-size:13px;color:#6b7280">O link vale por 24 horas. Se você não criou esta conta, ignore este e-mail.</p>
            """));

    public static MensagemEmail TentativaCadastroComEmailExistente(string nome, string linkRedefinir) => new(
        "Tentativa de cadastro com seu e-mail",
        Moldura("Você já tem uma conta", $"""
            <p>Olá, {E(nome)}.</p>
            <p>Alguém tentou criar uma conta com este e-mail, mas ele já está cadastrado. Se foi você e não lembra a senha, redefina:</p>
            {Botao(linkRedefinir, "Redefinir senha")}
            <p style="font-size:13px;color:#6b7280">Se não foi você, nenhuma ação é necessária.</p>
            """));

    public static MensagemEmail RedefinicaoSenha(string nome, string link) => new(
        "Redefinição de senha",
        Moldura("Redefinir senha", $"""
            <p>Olá, {E(nome)}.</p>
            <p>Recebemos um pedido para redefinir sua senha:</p>
            {Botao(link, "Criar nova senha")}
            <p style="font-size:13px;color:#6b7280">O link vale por 1 hora e só pode ser usado uma vez. Se você não pediu, ignore este e-mail: sua senha continua a mesma.</p>
            """));

    public static MensagemEmail SenhaAlterada(string nome) => new(
        "Sua senha foi alterada",
        Moldura("Senha alterada", $"""
            <p>Olá, {E(nome)}.</p>
            <p>A senha da sua conta foi alterada e as sessões abertas em outros dispositivos foram encerradas.</p>
            <p>Se não foi você, redefina a senha imediatamente e fale com nosso atendimento.</p>
            """));

    public static MensagemEmail MudancaStatusPedido(string nome, Pedido pedido, string linkPedido)
    {
        var (titulo, texto) = pedido.Status switch
        {
            StatusPedido.AguardandoPagamento => ("Pedido recebido", "Recebemos seu pedido. Assim que o pagamento for confirmado, começamos a separar seus produtos."),
            StatusPedido.PagamentoEmAnalise => ("Pagamento em análise", "Seu pagamento está em análise pela operadora. Avisaremos assim que houver uma resposta."),
            StatusPedido.Pago => ("Pagamento confirmado", "Seu pagamento foi confirmado. Já estamos preparando seu pedido."),
            StatusPedido.EmSeparacao => ("Pedido em separação", "Seu pedido está sendo separado e embalado."),
            StatusPedido.Enviado => ("Pedido enviado", $"Seu pedido foi enviado. Código de rastreio: <strong>{E(pedido.CodigoRastreio)}</strong>."),
            StatusPedido.Entregue => ("Pedido entregue", "Seu pedido foi entregue. Obrigado pela compra!"),
            StatusPedido.Cancelado => ("Pedido cancelado", $"Seu pedido foi cancelado. Motivo: {E(pedido.MotivoCancelamento)}."),
            _ => ("Atualização do pedido", "Seu pedido foi atualizado.")
        };

        var linhas = string.Join("", pedido.Itens.Select(i =>
            $"""<tr><td style="padding:4px 0">{i.Quantidade}× {E(i.NomeProduto)}</td><td style="text-align:right">{i.Total.ToString("C", PtBr)}</td></tr>"""));

        return new MensagemEmail(
            $"Pedido nº {pedido.Numero} — {titulo}",
            Moldura($"{titulo} — pedido nº {pedido.Numero}", $"""
                <p>Olá, {E(nome)}.</p>
                <p>{texto}</p>
                <table width="100%" style="font-size:14px;border-top:1px solid #e5e7eb;margin-top:12px;padding-top:8px">
                {linhas}
                <tr><td style="padding-top:8px">Frete ({E(pedido.FreteDescricao)})</td><td style="text-align:right;padding-top:8px">{pedido.ValorFrete.ToString("C", PtBr)}</td></tr>
                <tr><td><strong>Total</strong></td><td style="text-align:right"><strong>{pedido.Total.ToString("C", PtBr)}</strong></td></tr>
                </table>
                {Botao(linkPedido, "Ver pedido")}
                """));
    }
}
