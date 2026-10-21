using PortalVendas.Domain.Usuarios;

namespace PortalVendas.Domain.Pedidos;

/// <summary>Cópia do endereço no momento da compra (o cliente pode editar o original depois).</summary>
public class EnderecoEntrega
{
    private EnderecoEntrega() { }   // EF Core

    public EnderecoEntrega(string destinatario, string cep, string logradouro, string numero, string? complemento,
        string bairro, string cidade, string uf)
    {
        Destinatario = destinatario;
        Cep = cep;
        Logradouro = logradouro;
        Numero = numero;
        Complemento = complemento;
        Bairro = bairro;
        Cidade = cidade;
        Uf = uf;
    }

    public static EnderecoEntrega De(Endereco e) =>
        new(e.Destinatario, e.Cep, e.Logradouro, e.Numero, e.Complemento, e.Bairro, e.Cidade, e.Uf);

    public string Destinatario { get; private set; } = string.Empty;
    public string Cep { get; private set; } = string.Empty;
    public string Logradouro { get; private set; } = string.Empty;
    public string Numero { get; private set; } = string.Empty;
    public string? Complemento { get; private set; }
    public string Bairro { get; private set; } = string.Empty;
    public string Cidade { get; private set; } = string.Empty;
    public string Uf { get; private set; } = string.Empty;

    public string Resumo =>
        $"{Logradouro}, {Numero}{(string.IsNullOrWhiteSpace(Complemento) ? "" : " - " + Complemento)} — {Bairro}, {Cidade}/{Uf} — CEP {Comum.Cep.Formatar(Cep)}";
}
