using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Frete;
using PortalVendas.Application.Pagamentos;
using PortalVendas.Infrastructure.Dados;
using PortalVendas.Infrastructure.Frete;
using PortalVendas.Infrastructure.Jobs;
using PortalVendas.Infrastructure.Licenciamento;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Infrastructure.Pagamentos;
using PortalVendas.Infrastructure.Servicos;
using PortalVendas.Infrastructure.WhatsApp;
using PortalVendas.Application.WhatsApp;

namespace PortalVendas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfraestrutura(this IServiceCollection services, IConfiguration configuracao, IHostEnvironment ambiente)
    {
        var conexao = configuracao.GetConnectionString("PortalVendas")
            ?? throw new InvalidOperationException("Connection string 'PortalVendas' não configurada.");

        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseSqlServer(conexao, sql => sql.CommandTimeout(30));
            if (ambiente.IsDevelopment()) o.EnableDetailedErrors();
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddMemoryCache();

        services.Configure<LojaOptions>(configuracao.GetSection(LojaOptions.Secao));
        services.Configure<MercadoPagoOptions>(configuracao.GetSection(MercadoPagoOptions.Secao));
        services.Configure<MelhorEnvioOptions>(configuracao.GetSection(MelhorEnvioOptions.Secao));
        services.Configure<SmtpOptions>(configuracao.GetSection(SmtpOptions.Secao));
        services.Configure<ImagensOptions>(configuracao.GetSection(ImagensOptions.Secao));
        services.Configure<LicencaOptions>(configuracao.GetSection(LicencaOptions.Secao));
        services.AddSingleton<ILicencaLoja, ServicoLicenca>();

        services.AddHttpClient<IGatewayPagamento, MercadoPagoGateway>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<MercadoPagoOptions>>().Value;
            http.BaseAddress = new Uri(o.UrlBase.EndsWith('/') ? o.UrlBase : o.UrlBase + "/");
            http.Timeout = TimeSpan.FromSeconds(Math.Max(5, o.TimeoutSegundos));
        });

        services.AddHttpClient<IProvedorFrete, MelhorEnvioProvider>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<MelhorEnvioOptions>>().Value;
            http.BaseAddress = new Uri(o.UrlBase.EndsWith('/') ? o.UrlBase : o.UrlBase + "/");
            http.Timeout = TimeSpan.FromSeconds(Math.Max(2, o.TimeoutSegundos));
        });

        services.AddHttpClient<ICepService, ViaCepService>(http =>
        {
            http.BaseAddress = new Uri("https://viacep.com.br/");
            http.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<IHashSenha, HashSenha>();
        services.AddSingleton<IProtetorDados, ProtetorDados>();
        services.AddSingleton<ILinksAplicacao, LinksAplicacao>();
        services.AddSingleton<IArmazenamentoImagens>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ImagensOptions>>().Value;
            var pasta = Path.IsPathRooted(o.Pasta) ? o.Pasta : Path.Combine(ambiente.ContentRootPath, o.Pasta);
            return new ArmazenamentoImagensDisco(pasta, sp.GetRequiredService<ILogger<ArmazenamentoImagensDisco>>());
        });

        services.AddHostedService<ManutencaoJob>();

        // WhatsApp: conectores (Meta/Evolution), IA e fila de mensagens
        services.AddHttpClient(FabricaConectorWhatsApp.NomeCliente, http => http.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient(AssistenteClaude.NomeCliente, http =>
        {
            http.BaseAddress = new Uri("https://api.anthropic.com/");
            http.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IFabricaConectorWhatsApp, FabricaConectorWhatsApp>();
        services.AddScoped<IAssistenteVendas, AssistenteClaude>();
        services.AddSingleton<FilaWhatsApp>();
        services.AddSingleton<IFilaWhatsApp>(sp => sp.GetRequiredService<FilaWhatsApp>());
        services.AddHostedService<ProcessadorWhatsAppJob>();

        services.AddHealthChecks().AddCheck<BancoHealthCheck>("banco", HealthStatus.Unhealthy, new[] { "pronto" });
        return services;
    }
}

public class BancoHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;
    public BancoHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await _db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Sem conexão com o banco de dados.");
}
