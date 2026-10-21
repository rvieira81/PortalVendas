namespace PortalVendas.Domain.Comum;

/// <summary>Validação e formatação de CPF.</summary>
public static class Cpf
{
    public static string Normalizar(string? valor) => Texto.SomenteDigitos(valor);

    public static bool EhValido(string? valor)
    {
        var cpf = Normalizar(valor);
        if (cpf.Length != 11) return false;
        // Sequências repetidas (000.000.000-00, 111...) passam no cálculo mas são inválidas
        if (cpf.All(c => c == cpf[0])) return false;

        var digitos = cpf.Select(c => c - '0').ToArray();

        var soma = 0;
        for (var i = 0; i < 9; i++) soma += digitos[i] * (10 - i);
        var resto = soma % 11;
        var dv1 = resto < 2 ? 0 : 11 - resto;
        if (digitos[9] != dv1) return false;

        soma = 0;
        for (var i = 0; i < 10; i++) soma += digitos[i] * (11 - i);
        resto = soma % 11;
        var dv2 = resto < 2 ? 0 : 11 - resto;
        return digitos[10] == dv2;
    }

    public static string Formatar(string? valor)
    {
        var cpf = Normalizar(valor);
        return cpf.Length == 11 ? $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}" : cpf;
    }

    /// <summary>LGPD: exibe só os dígitos do meio — ***.456.789-**</summary>
    public static string Mascarar(string? valor)
    {
        var cpf = Normalizar(valor);
        return cpf.Length == 11 ? $"***.{cpf[3..6]}.{cpf[6..9]}-**" : "***";
    }
}

/// <summary>Validação e formatação de CEP.</summary>
public static class Cep
{
    public static string Normalizar(string? valor) => Texto.SomenteDigitos(valor);

    public static bool EhValido(string? valor)
    {
        var cep = Normalizar(valor);
        return cep.Length == 8 && cep != "00000000";
    }

    public static string Formatar(string? valor)
    {
        var cep = Normalizar(valor);
        return cep.Length == 8 ? $"{cep[..5]}-{cep[5..]}" : cep;
    }
}

public static class Telefone
{
    public static string Normalizar(string? valor) => Texto.SomenteDigitos(valor);

    /// <summary>DDD + número: 10 dígitos (fixo) ou 11 (celular).</summary>
    public static bool EhValido(string? valor)
    {
        var t = Normalizar(valor);
        return t.Length is 10 or 11 && t[0] != '0';
    }

    public static string Mascarar(string? valor)
    {
        var t = Normalizar(valor);
        return t.Length >= 10 ? $"({t[..2]}) *****-{t[^4..]}" : "***";
    }

    public static string Formatar(string? valor)
    {
        var t = Normalizar(valor);
        return t.Length switch
        {
            11 => $"({t[..2]}) {t[2..7]}-{t[7..]}",
            10 => $"({t[..2]}) {t[2..6]}-{t[6..]}",
            _ => t
        };
    }
}

public static class Uf
{
    public static readonly IReadOnlySet<string> Validas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AC","AL","AP","AM","BA","CE","DF","ES","GO","MA","MT","MS","MG","PA",
        "PB","PR","PE","PI","RJ","RN","RS","RO","RR","SC","SP","SE","TO"
    };

    public static bool EhValida(string? uf) => uf is { Length: 2 } && Validas.Contains(uf);
}
