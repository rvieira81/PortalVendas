namespace PortalVendas.Domain.Pagamentos;

/// <summary>Registro de cada webhook recebido (auditoria e deduplicação).</summary>
public class NotificacaoGateway
{
    public long Id { get; set; }
    public string? RequestId { get; set; }
    public string Topico { get; set; } = string.Empty;
    public string RecursoId { get; set; } = string.Empty;
    public string? Corpo { get; set; }
    public bool AssinaturaValida { get; set; }
    public bool Processada { get; set; }
    public string? Erro { get; set; }
    public DateTime RecebidaEm { get; set; }
    public DateTime? ProcessadaEm { get; set; }
}
