using Microsoft.Extensions.DependencyInjection;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Admin;
using PortalVendas.Application.Carrinhos;
using PortalVendas.Application.Catalogo;
using PortalVendas.Application.Contas;
using PortalVendas.Application.Emails;
using PortalVendas.Application.Frete;
using PortalVendas.Application.Pagamentos;
using PortalVendas.Application.Pedidos;
using PortalVendas.Application.WhatsApp;

namespace PortalVendas.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAplicacao(this IServiceCollection services)
    {
        services.AddSingleton<IRelogio, RelogioSistema>();

        services.AddScoped<ConfiguracaoLojaService>();
        services.AddScoped<IdentidadeLojaService>();

        // Módulo WhatsApp (só atende com o módulo na licença)
        services.AddScoped<ConfiguracaoWhatsAppService>();
        services.AddScoped<EnvioWhatsAppService>();
        services.AddScoped<AtendimentoWhatsAppService>();
        services.AddScoped<ProcessadorWhatsAppService>();
        services.AddScoped<ConversasWhatsAppService>();
        services.AddScoped<IAvisoPedidoWhatsApp, AvisoPedidoWhatsApp>();
        services.AddScoped<CatalogoService>();
        services.AddScoped<CarrinhoService>();
        services.AddScoped<FreteService>();
        services.AddScoped<EstoqueService>();
        services.AddScoped<NotificadorPedidos>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<PedidoService>();
        services.AddScoped<PagamentoService>();
        services.AddScoped<ManutencaoService>();
        services.AddScoped<ContaService>();
        services.AddScoped<EnderecoService>();
        services.AddScoped<ProdutoAdminService>();
        services.AddScoped<CategoriaAdminService>();
        services.AddScoped<FaixaFreteService>();
        services.AddScoped<DashboardService>();
        return services;
    }
}
