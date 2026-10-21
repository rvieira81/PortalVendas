namespace PortalVendas.Domain.Comum;

public static class DiasUteis
{
    /// <summary>Soma dias úteis (segunda a sexta). Feriados não são considerados.</summary>
    public static DateOnly Somar(DateOnly data, int dias)
    {
        var resultado = data;
        var adicionados = 0;
        while (adicionados < dias)
        {
            resultado = resultado.AddDays(1);
            if (resultado.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                adicionados++;
        }
        return resultado;
    }
}
