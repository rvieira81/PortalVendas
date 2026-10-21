namespace PortalVendas.Domain.Usuarios;

public enum PerfilUsuario : byte
{
    Cliente = 1,
    Administrador = 2
}

public class Usuario
{
    public const int TentativasAntesDoBloqueio = 5;
    public static readonly TimeSpan DuracaoBloqueio = TimeSpan.FromMinutes(15);

    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailConfirmado { get; set; }
    public string? Cpf { get; set; }
    public string? Telefone { get; set; }
    public PerfilUsuario Perfil { get; set; } = PerfilUsuario.Cliente;
    public string SenhaHash { get; set; } = string.Empty;
    public Guid CarimboSeguranca { get; set; } = Guid.NewGuid();
    public int FalhasLogin { get; set; }
    public DateTime? BloqueadoAte { get; set; }
    public string? TotpSegredoProtegido { get; set; }
    public bool TotpAtivo { get; set; }
    public DateTime? AceitouTermosEm { get; set; }
    public bool Anonimizado { get; set; }
    public DateTime? UltimoLoginEm { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AlteradoEm { get; set; }

    public List<Endereco> Enderecos { get; set; } = new();

    public bool EhAdministrador => Perfil == PerfilUsuario.Administrador;

    public bool EstaBloqueado(DateTime agoraUtc) => BloqueadoAte is not null && BloqueadoAte > agoraUtc;

    /// <summary>RF05: bloqueia por 15 minutos após 5 falhas seguidas.</summary>
    public void RegistrarFalhaLogin(DateTime agoraUtc)
    {
        FalhasLogin++;
        if (FalhasLogin >= TentativasAntesDoBloqueio)
        {
            BloqueadoAte = agoraUtc.Add(DuracaoBloqueio);
            FalhasLogin = 0;
        }
        AlteradoEm = agoraUtc;
    }

    public void RegistrarLoginComSucesso(DateTime agoraUtc)
    {
        FalhasLogin = 0;
        BloqueadoAte = null;
        UltimoLoginEm = agoraUtc;
    }

    /// <summary>Troca o carimbo: todas as sessões abertas deixam de valer.</summary>
    public void InvalidarSessoes() => CarimboSeguranca = Guid.NewGuid();

    /// <summary>LGPD: remove os dados pessoais preservando o vínculo com os pedidos (documentos fiscais).</summary>
    public void Anonimizar(DateTime agoraUtc)
    {
        Nome = "Cliente removido";
        Email = $"removido-{Id}@anonimo.invalid";
        EmailConfirmado = false;
        Cpf = null;
        Telefone = null;
        SenhaHash = "!";   // nenhum hash válido começa com "!": login impossível
        TotpSegredoProtegido = null;
        TotpAtivo = false;
        Anonimizado = true;
        InvalidarSessoes();
        AlteradoEm = agoraUtc;
    }
}
