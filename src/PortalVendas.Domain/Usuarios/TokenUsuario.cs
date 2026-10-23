namespace PortalVendas.Domain.Usuarios;

public enum TipoToken : byte
{
    ConfirmacaoEmail = 1,
    RedefinicaoSenha = 2,
    /// <summary>Link enviado no WhatsApp para o cliente pagar com cartão ou boleto no site sem criar senha.</summary>
    AcessoWhatsApp = 3
}

public class TokenUsuario
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public TipoToken Tipo { get; set; }
    /// <summary>SHA-256 do token, em hexadecimal maiúsculo. O token em si só existe no link do e-mail.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiraEm { get; set; }
    public DateTime? UsadoEm { get; set; }
    public DateTime CriadoEm { get; set; }

    public bool EstaValido(DateTime agoraUtc) => UsadoEm is null && ExpiraEm > agoraUtc;
}
