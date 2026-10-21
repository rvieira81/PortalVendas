namespace PortalVendas.Domain.Comum;

/// <summary>Violação de uma regra que o domínio nunca deve aceitar (erro de programação ou dado corrompido).</summary>
public sealed class DomainException : Exception
{
    public DomainException(string mensagem) : base(mensagem) { }
}
