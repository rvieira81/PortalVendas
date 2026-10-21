namespace PortalVendas.Application.Comum;

/// <summary>Conversão para o horário de Brasília na exibição (o banco guarda UTC).</summary>
public static class Horario
{
    public static readonly TimeZoneInfo Brasilia = ObterFuso();

    private static TimeZoneInfo ObterFuso()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
    }

    public static DateTime ParaBrasilia(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Brasilia);

    public static DateTimeOffset ParaBrasiliaOffset(DateTime utc)
    {
        var local = ParaBrasilia(utc);
        return new DateTimeOffset(local, Brasilia.GetUtcOffset(local));
    }

    public static DateOnly HojeEmBrasilia(DateTime agoraUtc) => DateOnly.FromDateTime(ParaBrasilia(agoraUtc));

    /// <summary>Converte uma data/hora local de Brasília para UTC.</summary>
    public static DateTime DeBrasiliaParaUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Brasilia);
}
