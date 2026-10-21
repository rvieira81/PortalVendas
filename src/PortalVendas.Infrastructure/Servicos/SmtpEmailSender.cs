using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortalVendas.Application.Abstracoes;

namespace PortalVendas.Infrastructure.Servicos;

public class SmtpOptions
{
    public const string Secao = "Smtp";

    public string? Host { get; set; }
    public int Porta { get; set; } = 587;
    public bool UsarSsl { get; set; } = true;
    public string? Usuario { get; set; }
    /// <summary>Segredo: nunca no repositório.</summary>
    public string? Senha { get; set; }
    public string Remetente { get; set; } = "nao-responda@sualoja.com.br";
    public string NomeRemetente { get; set; } = "Loja";
    /// <summary>Desenvolvimento: sem Host, grava os e-mails como .html nesta pasta em vez de enviar.</summary>
    public string? PastaArquivos { get; set; }
}

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _opcoes;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> opcoes, ILogger<SmtpEmailSender> logger)
    {
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public async Task EnviarAsync(string para, string assunto, string html, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_opcoes.Host))
            {
                await GravarEmArquivoAsync(para, assunto, html, ct);
                return;
            }

            using var mensagem = new MailMessage
            {
                From = new MailAddress(_opcoes.Remetente, _opcoes.NomeRemetente, Encoding.UTF8),
                Subject = assunto,
                SubjectEncoding = Encoding.UTF8,
                Body = html,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = true
            };
            mensagem.To.Add(para);

            using var cliente = new SmtpClient(_opcoes.Host, _opcoes.Porta)
            {
                EnableSsl = _opcoes.UsarSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000
            };
            if (!string.IsNullOrEmpty(_opcoes.Usuario))
                cliente.Credentials = new NetworkCredential(_opcoes.Usuario, _opcoes.Senha);

            await cliente.SendMailAsync(mensagem, ct);
            _logger.LogInformation("E-mail \"{Assunto}\" enviado", assunto);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O destinatário não vai para o log (LGPD)
            _logger.LogError(ex, "Falha ao enviar o e-mail \"{Assunto}\"", assunto);
        }
    }

    private async Task GravarEmArquivoAsync(string para, string assunto, string html, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.PastaArquivos))
        {
            _logger.LogWarning("SMTP não configurado: e-mail \"{Assunto}\" descartado", assunto);
            return;
        }
        Directory.CreateDirectory(_opcoes.PastaArquivos);
        var nome = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..8]}.html";
        var conteudo = $"<!-- Para: {WebUtility.HtmlEncode(para)} | Assunto: {WebUtility.HtmlEncode(assunto)} -->\n{html}";
        await File.WriteAllTextAsync(Path.Combine(_opcoes.PastaArquivos, nome), conteudo, Encoding.UTF8, ct);
        _logger.LogInformation("E-mail \"{Assunto}\" gravado em {Pasta} (modo desenvolvimento)", assunto, _opcoes.PastaArquivos);
    }
}
