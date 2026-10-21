namespace PortalVendas.Domain.Frete;

/// <summary>Tabela de contingência: usada quando a cotação em tempo real não está disponível.</summary>
public class FaixaFrete
{
    public int Id { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string CepInicio { get; set; } = string.Empty;
    public string CepFim { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public int PrazoDias { get; set; }
    public bool Ativa { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime? AlteradoEm { get; set; }

    public bool Atende(string cep) =>
        Ativa && string.CompareOrdinal(cep, CepInicio) >= 0 && string.CompareOrdinal(cep, CepFim) <= 0;
}

public class Configuracao
{
    public string Chave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public DateTime? AlteradoEm { get; set; }
    public string? AlteradoPor { get; set; }
}
