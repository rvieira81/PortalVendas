using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using PortalVendas.Application;
using PortalVendas.Application.Contas;
using PortalVendas.Infrastructure;
using PortalVendas.Infrastructure.Pagamentos;
using PortalVendas.Infrastructure.Servicos;
using PortalVendas.Web.Infra;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((contexto, servicos, log) => log
        .ReadFrom.Configuration(contexto.Configuration)
        .ReadFrom.Services(servicos)
        .Enrich.FromLogContext());

    var culturaPtBr = new CultureInfo("pt-BR");
    CultureInfo.DefaultThreadCurrentCulture = culturaPtBr;
    CultureInfo.DefaultThreadCurrentUICulture = culturaPtBr;

    // ---------------------------------------------------------------- camadas
    builder.Services.AddAplicacao();
    builder.Services.AddInfraestrutura(builder.Configuration, builder.Environment);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ContextoCarrinho>();
    builder.Services.AddScoped<ServicoSessao>();

    // ---------------------------------------------------------------- Data Protection (cookies, antiforgery, segredo TOTP)
    var pastaChaves = builder.Configuration["DataProtection:PastaChaves"];
    if (string.IsNullOrWhiteSpace(pastaChaves)) pastaChaves = Path.Combine(builder.Environment.ContentRootPath, "keys");
    var dataProtection = builder.Services.AddDataProtection()
        .SetApplicationName("PortalVendas")
        .PersistKeysToFileSystem(new DirectoryInfo(pastaChaves));
    // DPAPI da máquina: ideal no IIS próprio. No Azure App Service o site pode mudar de máquina,
    // então lá se usa DataProtection:ProtegerComDpapi = false (as chaves ficam em D:\home, que é privado do site).
    var protegerComDpapi = builder.Configuration.GetValue("DataProtection:ProtegerComDpapi", true);
    if (OperatingSystem.IsWindows() && protegerComDpapi) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);

    // ---------------------------------------------------------------- MVC
    builder.Services.AddControllersWithViews(o =>
    {
        // CSRF: todo POST exige token antiforgery (o webhook usa [IgnoreAntiforgeryToken] e tem assinatura própria)
        o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    });
    builder.Services.AddAntiforgery(o =>
    {
        o.HeaderName = "RequestVerificationToken";
        o.Cookie.Name = "pv_af";
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
    });

    // ---------------------------------------------------------------- autenticação e autorização
    builder.Services.AddAuthentication(Esquemas.Principal)
        .AddCookie(Esquemas.Principal, o =>
        {
            o.Cookie.Name = "pv_sessao";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.LoginPath = "/conta/entrar";
            o.LogoutPath = "/conta/sair";
            o.AccessDeniedPath = "/conta/acesso-negado";
            o.ExpireTimeSpan = TimeSpan.FromHours(2);
            o.SlidingExpiration = true;
            o.Events.OnValidatePrincipal = ValidadorSessao.ValidarAsync;
            o.Events.OnRedirectToLogin = contexto =>
            {
                // Chamadas via fetch (pagamento com cartão, consulta de status) recebem 401 em vez de redirecionamento
                if (contexto.Request.Headers.Accept.ToString().Contains("application/json"))
                    contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else
                    contexto.Response.Redirect(contexto.RedirectUri);
                return Task.CompletedTask;
            };
        })
        .AddCookie(Esquemas.PreMfa, o =>
        {
            o.Cookie.Name = "pv_mfa";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            o.SlidingExpiration = false;
        });

    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(Politicas.Admin, p => p
            .RequireAuthenticatedUser()
            .AddAuthenticationSchemes(Esquemas.Principal)
            .RequireRole(Perfis.Administrador)
            .RequireClaim(Reivindicacoes.MetodoAutenticacao, Reivindicacoes.Mfa))
        .AddPolicy(Politicas.AdminConfigurarMfa, p => p
            .RequireAuthenticatedUser()
            .RequireRole(Perfis.Administrador));

    // ---------------------------------------------------------------- rate limiting (RNF06)
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
        o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        o.AddPolicy("consulta", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        o.AddPolicy("webhook", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });

    if (!builder.Environment.IsDevelopment())
    {
        builder.Services.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(365);
            o.IncludeSubDomains = true;
        });
    }

    var app = builder.Build();

    // ---------------------------------------------------------------- linha de comando: criar administrador
    if (args.Length > 0 && args[0].Equals("criar-admin", StringComparison.OrdinalIgnoreCase))
        return await LinhaDeComando.CriarAdministradorAsync(app, args);

    // ---------------------------------------------------------------- pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/erro");
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/erro/{0}");
    app.UseHttpsRedirection();
    app.UseMiddleware<CabecalhosSegurancaMiddleware>();

    app.UseRequestLocalization(o =>
    {
        o.DefaultRequestCulture = new RequestCulture(culturaPtBr);
        o.SupportedCultures = new[] { culturaPtBr };
        o.SupportedUICultures = new[] { culturaPtBr };
        o.RequestCultureProviders.Clear();
    });

    app.UseStaticFiles();
    var imagens = app.Services.GetRequiredService<IOptions<ImagensOptions>>().Value;
    var pastaImagens = Path.IsPathRooted(imagens.Pasta) ? imagens.Pasta : Path.Combine(app.Environment.ContentRootPath, imagens.Pasta);
    Directory.CreateDirectory(pastaImagens);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(pastaImagens),
        RequestPath = imagens.CaminhoPublico,
        OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "public,max-age=2592000,immutable"
    });

    // Licença: domínio autorizado (depois dos arquivos estáticos, antes de qualquer página)
    app.UseMiddleware<LicencaMiddleware>();

    app.UseSerilogRequestLogging(o =>
    {
        o.EnrichDiagnosticContext = (diag, http) =>
        {
            diag.Set("UsuarioId", http.User.UsuarioId());
            diag.Set("RequestId", http.TraceIdentifier);
        };
    });

    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => true });
    app.MapControllerRoute("areas", "{area:exists}/{controller=Painel}/{action=Index}/{id?}");
    app.MapControllers();
    app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

    var licenca = app.Services.GetRequiredService<PortalVendas.Application.Licenciamento.ILicencaLoja>();
    if (!licenca.Estado.Ativa)
        Log.Warning("Licença: {Mensagem} A vitrine só abre em localhost até a licença ser instalada (painel → Identidade da loja).", licenca.Estado.Mensagem);
    var urlBase = app.Services.GetRequiredService<IOptions<LojaOptions>>().Value.UrlBase;
    if (licenca.Estado.Ativa && Uri.TryCreate(urlBase, UriKind.Absolute, out var uriBase) && !licenca.HostAutorizado(uriBase.Host, requisicaoLocal: false))
        Log.Warning("Loja:UrlBase ({UrlBase}) não está entre os domínios da licença: os links dos e-mails vão abrir a página de loja indisponível", urlBase);

    var mp = app.Services.GetRequiredService<IOptions<MercadoPagoOptions>>().Value;
    if (string.IsNullOrWhiteSpace(mp.AccessToken) || string.IsNullOrWhiteSpace(mp.SegredoWebhook))
        Log.Warning("Mercado Pago sem AccessToken ou SegredoWebhook: pagamentos e webhooks não vão funcionar");

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "O site parou por um erro na inicialização");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Usado pelos testes de integração (WebApplicationFactory).</summary>
public partial class Program { }

internal static class LinhaDeComando
{
    /// <summary>
    /// dotnet PortalVendas.Web.dll criar-admin admin@loja.com.br "Nome do Admin"
    /// A senha é pedida no console, sem eco, e nunca passa pela linha de comando.
    /// </summary>
    public static async Task<int> CriarAdministradorAsync(WebApplication app, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Uso: PortalVendas.Web criar-admin <email> \"<nome>\"");
            return 2;
        }

        var senha = LerSenha("Senha (mínimo 12 caracteres, letras e números): ");
        var confirmacao = LerSenha("Confirme a senha: ");
        if (senha != confirmacao)
        {
            Console.WriteLine("As senhas não conferem.");
            return 2;
        }

        using var scope = app.Services.CreateScope();
        var contas = scope.ServiceProvider.GetRequiredService<ContaService>();
        var resultado = await contas.CriarAdministradorAsync(args[1], args[2], senha);
        Console.WriteLine(resultado.Sucesso
            ? "Administrador criado. No primeiro acesso a /conta/entrar será pedido para configurar o aplicativo autenticador."
            : $"Erro: {resultado.Erro}");
        return resultado.Sucesso ? 0 : 1;
    }

    private static string LerSenha(string rotulo)
    {
        // Entrada redirecionada (pipe/script): lê a linha inteira
        if (Console.IsInputRedirected)
        {
            Console.Write(rotulo);
            return Console.ReadLine() ?? string.Empty;
        }

        // Descarta teclas que sobraram no buffer (ex.: o Enter do próprio comando)
        while (Console.KeyAvailable) Console.ReadKey(intercept: true);

        while (true)
        {
            Console.Write(rotulo);
            var senha = new System.Text.StringBuilder();
            while (true)
            {
                var tecla = Console.ReadKey(intercept: true);
                if (tecla.Key == ConsoleKey.Enter) break;
                if (tecla.Key == ConsoleKey.Backspace)
                {
                    if (senha.Length > 0)
                    {
                        senha.Length--;
                        Console.Write("\b \b");
                    }
                    continue;
                }
                if (!char.IsControl(tecla.KeyChar))
                {
                    senha.Append(tecla.KeyChar);
                    Console.Write('*');
                }
            }
            Console.WriteLine();
            if (senha.Length > 0) return senha.ToString();
            Console.WriteLine("Senha vazia. Digite novamente.");
        }
    }
}
