using PortalVendas.Domain.Comum;

namespace PortalVendas.Domain.Usuarios;

public class Endereco
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string Apelido { get; set; } = string.Empty;
    public string Destinatario { get; set; } = string.Empty;
    public string Cep { get; set; } = string.Empty;
    public string Logradouro { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string? Complemento { get; set; }
    public string Bairro { get; set; } = string.Empty;
    public string Cidade { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public bool Padrao { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AlteradoEm { get; set; }

    public string Resumo =>
        $"{Logradouro}, {Numero}{(string.IsNullOrWhiteSpace(Complemento) ? "" : " - " + Complemento)} — {Bairro}, {Cidade}/{Uf} — CEP {Comum.Cep.Formatar(Cep)}";

    public void Normalizar()
    {
        Apelido = Texto.Limpar(Apelido) ?? "Endereço";
        Destinatario = Texto.Limpar(Destinatario) ?? string.Empty;
        Cep = Comum.Cep.Normalizar(Cep);
        Logradouro = Texto.Limpar(Logradouro) ?? string.Empty;
        Numero = Texto.Limpar(Numero) ?? string.Empty;
        Complemento = Texto.Limpar(Complemento);
        Bairro = Texto.Limpar(Bairro) ?? string.Empty;
        Cidade = Texto.Limpar(Cidade) ?? string.Empty;
        Uf = (Uf ?? string.Empty).Trim().ToUpperInvariant();
    }

    public IReadOnlyList<string> Validar()
    {
        var erros = new List<string>();
        if (Apelido.Length > 50) erros.Add("O apelido pode ter no máximo 50 caracteres.");
        if (string.IsNullOrWhiteSpace(Destinatario)) erros.Add("Informe quem vai receber.");
        if (!Comum.Cep.EhValido(Cep)) erros.Add("CEP inválido.");
        if (string.IsNullOrWhiteSpace(Logradouro)) erros.Add("Informe a rua.");
        if (string.IsNullOrWhiteSpace(Numero)) erros.Add("Informe o número (use \"S/N\" se não houver).");
        if (string.IsNullOrWhiteSpace(Bairro)) erros.Add("Informe o bairro.");
        if (string.IsNullOrWhiteSpace(Cidade)) erros.Add("Informe a cidade.");
        if (!Comum.Uf.EhValida(Uf)) erros.Add("UF inválida.");
        if (Logradouro.Length > 200 || Numero.Length > 20 || (Complemento?.Length ?? 0) > 100
            || Bairro.Length > 100 || Cidade.Length > 100 || Destinatario.Length > 150)
            erros.Add("Algum campo do endereço passou do tamanho máximo.");
        return erros;
    }
}
