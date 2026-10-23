using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalVendas.Application.Abstracoes;
using PortalVendas.Application.Comum;
using PortalVendas.Application.Emails;
using PortalVendas.Domain.Comum;
using PortalVendas.Domain.Usuarios;

namespace PortalVendas.Application.Contas;

public record ComandoCadastro(string Nome, string Email, string Cpf, string Telefone, string Senha, bool AceitouTermos);

public enum SituacaoLogin
{
    Sucesso,
    CredenciaisInvalidas,
    Bloqueado,
    RequerCodigoTotp,
    RequerConfiguracaoTotp
}

public record ResultadoLogin(SituacaoLogin Situacao, Usuario? Usuario, DateTime? BloqueadoAte = null);

public class ContaService
{
    public static readonly TimeSpan ValidadeConfirmacaoEmail = TimeSpan.FromHours(24);
    public static readonly TimeSpan ValidadeRedefinicaoSenha = TimeSpan.FromHours(1);

    private readonly IAppDbContext _db;
    private readonly IHashSenha _hash;
    private readonly IProtetorDados _protetor;
    private readonly IEmailSender _email;
    private readonly ILinksAplicacao _links;
    private readonly IRelogio _relogio;
    private readonly ILogger<ContaService> _logger;

    // Hash fictício para gastar o mesmo tempo quando o e-mail não existe (evita descobrir e-mails pelo tempo de resposta)
    private readonly Lazy<string> _hashFicticio;

    public ContaService(IAppDbContext db, IHashSenha hash, IProtetorDados protetor, IEmailSender email, ILinksAplicacao links,
        IRelogio relogio, ILogger<ContaService> logger)
    {
        _db = db;
        _hash = hash;
        _protetor = protetor;
        _email = email;
        _links = links;
        _relogio = relogio;
        _logger = logger;
        _hashFicticio = new Lazy<string>(() => _hash.Gerar(Guid.NewGuid().ToString()));
    }

    // ================================================================== validações

    public static string NormalizarEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool EmailValido(string email)
    {
        if (email.Length is < 6 or > 254 || email.Contains(' ')) return false;
        try
        {
            var endereco = new MailAddress(email);
            return endereco.Address == email && endereco.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Mínimo de 8 caracteres, com letra e número.</summary>
    public static string? ValidarSenha(string? senha)
    {
        if (string.IsNullOrEmpty(senha) || senha.Length < 8) return "A senha deve ter pelo menos 8 caracteres.";
        if (senha.Length > 128) return "A senha pode ter no máximo 128 caracteres.";
        if (!senha.Any(char.IsLetter) || !senha.Any(char.IsDigit)) return "A senha deve ter letras e números.";
        return null;
    }

    // ================================================================== cadastro (RF05)

    /// <summary>
    /// Cria a conta e envia o e-mail de confirmação. Se o e-mail ou o CPF já existirem, responde igual
    /// (sem revelar que a conta existe) e avisa o dono do e-mail.
    /// </summary>
    public async Task<Resultado> CadastrarAsync(ComandoCadastro cmd, CancellationToken ct = default)
    {
        var nome = Texto.Limpar(cmd.Nome) ?? string.Empty;
        var email = NormalizarEmail(cmd.Email);
        var cpf = Cpf.Normalizar(cmd.Cpf);
        var telefone = Telefone.Normalizar(cmd.Telefone);

        if (nome.Length < 3 || !nome.Contains(' ')) return Resultado.Falha("Informe nome e sobrenome.");
        if (nome.Length > 150) return Resultado.Falha("O nome pode ter no máximo 150 caracteres.");
        if (!EmailValido(email)) return Resultado.Falha("E-mail inválido.");
        if (!Cpf.EhValido(cpf)) return Resultado.Falha("CPF inválido.");
        if (!Telefone.EhValido(telefone)) return Resultado.Falha("Telefone inválido. Informe DDD e número.");
        var erroSenha = ValidarSenha(cmd.Senha);
        if (erroSenha is not null) return Resultado.Falha(erroSenha);
        if (!cmd.AceitouTermos) return Resultado.Falha("É preciso aceitar os termos de uso e a política de privacidade.");

        var existentePorEmail = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (existentePorEmail is not null)
        {
            var token = await CriarTokenAsync(existentePorEmail.Id, TipoToken.RedefinicaoSenha, ValidadeRedefinicaoSenha, ct);
            var aviso = ModelosEmail.TentativaCadastroComEmailExistente(existentePorEmail.Nome, _links.RedefinirSenha(token));
            await _email.EnviarAsync(existentePorEmail.Email, aviso.Assunto, aviso.Html, ct);
            _logger.LogInformation("Tentativa de cadastro com e-mail já existente (usuário {UsuarioId})", existentePorEmail.Id);
            return Resultado.Ok();
        }

        if (await _db.Usuarios.AnyAsync(u => u.Cpf == cpf, ct))
        {
            _logger.LogWarning("Tentativa de cadastro com CPF já existente em outra conta");
            return Resultado.Ok();
        }

        var agora = _relogio.AgoraUtc;
        var usuario = new Usuario
        {
            Nome = nome,
            Email = email,
            Cpf = cpf,
            Telefone = telefone,
            Perfil = PerfilUsuario.Cliente,
            SenhaHash = _hash.Gerar(cmd.Senha),
            AceitouTermosEm = agora,
            CriadoEm = agora
        };
        _db.Usuarios.Add(usuario);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Cadastro simultâneo com o mesmo e-mail/CPF: as UNIQUE do banco barraram
            _db.ChangeTracker.Clear();
            return Resultado.Ok();
        }

        await EnviarConfirmacaoAsync(usuario, ct);
        _logger.LogInformation("Usuário {UsuarioId} cadastrado", usuario.Id);
        return Resultado.Ok();
    }

    public async Task ReenviarConfirmacaoAsync(int usuarioId, CancellationToken ct = default)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null || usuario.EmailConfirmado || usuario.Anonimizado) return;

        // No máximo um reenvio a cada 2 minutos
        var recente = await _db.TokensUsuario.AnyAsync(t => t.UsuarioId == usuarioId && t.Tipo == TipoToken.ConfirmacaoEmail
            && t.CriadoEm > _relogio.AgoraUtc.AddMinutes(-2), ct);
        if (recente) return;
        await EnviarConfirmacaoAsync(usuario, ct);
    }

    private async Task EnviarConfirmacaoAsync(Usuario usuario, CancellationToken ct)
    {
        var token = await CriarTokenAsync(usuario.Id, TipoToken.ConfirmacaoEmail, ValidadeConfirmacaoEmail, ct);
        var msg = ModelosEmail.ConfirmacaoCadastro(usuario.Nome, _links.ConfirmarEmail(token));
        await _email.EnviarAsync(usuario.Email, msg.Assunto, msg.Html, ct);
    }

    public async Task<Resultado> ConfirmarEmailAsync(string token, CancellationToken ct = default)
    {
        var registro = await ObterTokenValidoAsync(token, TipoToken.ConfirmacaoEmail, ct);
        if (registro?.Usuario is null) return Resultado.Falha("Link inválido ou expirado. Peça um novo e-mail de confirmação em Minha conta.");

        var agora = _relogio.AgoraUtc;
        registro.UsadoEm = agora;
        registro.Usuario.EmailConfirmado = true;
        registro.Usuario.AlteradoEm = agora;
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    // ================================================================== login (RF05, RNF05)

    public async Task<ResultadoLogin> AutenticarAsync(string email, string senha, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;
        var normalizado = NormalizarEmail(email);
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == normalizado && !u.Anonimizado, ct);

        if (usuario is null)
        {
            _hash.Verificar(_hashFicticio.Value, senha ?? string.Empty);
            return new ResultadoLogin(SituacaoLogin.CredenciaisInvalidas, null);
        }

        if (usuario.EstaBloqueado(agora))
            return new ResultadoLogin(SituacaoLogin.Bloqueado, null, usuario.BloqueadoAte);

        var verificacao = _hash.Verificar(usuario.SenhaHash, senha ?? string.Empty);
        if (verificacao == VerificacaoSenha.Falha)
        {
            usuario.RegistrarFalhaLogin(agora);
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Falha de login do usuário {UsuarioId}", usuario.Id);
            return usuario.EstaBloqueado(agora)
                ? new ResultadoLogin(SituacaoLogin.Bloqueado, null, usuario.BloqueadoAte)
                : new ResultadoLogin(SituacaoLogin.CredenciaisInvalidas, null);
        }

        if (verificacao == VerificacaoSenha.SucessoPrecisaRehash)
            usuario.SenhaHash = _hash.Gerar(senha!);

        if (usuario.EhAdministrador)
        {
            // A senha confere, mas o login só termina depois do segundo fator
            await _db.SaveChangesAsync(ct);
            return new ResultadoLogin(usuario.TotpAtivo ? SituacaoLogin.RequerCodigoTotp : SituacaoLogin.RequerConfiguracaoTotp, usuario);
        }

        usuario.RegistrarLoginComSucesso(agora);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Login do usuário {UsuarioId}", usuario.Id);
        return new ResultadoLogin(SituacaoLogin.Sucesso, usuario);
    }

    /// <summary>Segundo fator do administrador. Erros contam para o bloqueio, como a senha.</summary>
    public async Task<ResultadoLogin> VerificarTotpAsync(int usuarioId, string codigo, CancellationToken ct = default)
    {
        var agora = _relogio.AgoraUtc;
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId && u.Perfil == PerfilUsuario.Administrador, ct);
        if (usuario is null || !usuario.TotpAtivo || usuario.TotpSegredoProtegido is null)
            return new ResultadoLogin(SituacaoLogin.CredenciaisInvalidas, null);
        if (usuario.EstaBloqueado(agora))
            return new ResultadoLogin(SituacaoLogin.Bloqueado, null, usuario.BloqueadoAte);

        var segredo = _protetor.Desproteger(usuario.TotpSegredoProtegido);
        if (segredo is null || !Totp.Validar(segredo, codigo, agora))
        {
            usuario.RegistrarFalhaLogin(agora);
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Código TOTP inválido para o usuário {UsuarioId}", usuario.Id);
            return usuario.EstaBloqueado(agora)
                ? new ResultadoLogin(SituacaoLogin.Bloqueado, null, usuario.BloqueadoAte)
                : new ResultadoLogin(SituacaoLogin.CredenciaisInvalidas, null);
        }

        usuario.RegistrarLoginComSucesso(agora);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Login com segundo fator do administrador {UsuarioId}", usuario.Id);
        return new ResultadoLogin(SituacaoLogin.Sucesso, usuario);
    }

    /// <summary>Gera (ou devolve o pendente) segredo TOTP para o administrador configurar no aplicativo.</summary>
    public async Task<string?> PrepararTotpAsync(int usuarioId, CancellationToken ct = default)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null || !usuario.EhAdministrador || usuario.TotpAtivo) return null;

        if (usuario.TotpSegredoProtegido is not null)
        {
            var existente = _protetor.Desproteger(usuario.TotpSegredoProtegido);
            if (existente is not null) return existente;
        }

        var segredo = Totp.GerarSegredo();
        usuario.TotpSegredoProtegido = _protetor.Proteger(segredo);
        usuario.AlteradoEm = _relogio.AgoraUtc;
        await _db.SaveChangesAsync(ct);
        return segredo;
    }

    public async Task<Resultado> AtivarTotpAsync(int usuarioId, string codigo, CancellationToken ct = default)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null || !usuario.EhAdministrador || usuario.TotpSegredoProtegido is null)
            return Resultado.Falha("Configuração não iniciada.");

        var segredo = _protetor.Desproteger(usuario.TotpSegredoProtegido);
        if (segredo is null || !Totp.Validar(segredo, codigo, _relogio.AgoraUtc))
            return Resultado.Falha("Código incorreto. Confira o horário do celular e tente de novo.");

        usuario.TotpAtivo = true;
        usuario.RegistrarLoginComSucesso(_relogio.AgoraUtc);
        usuario.AlteradoEm = _relogio.AgoraUtc;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Segundo fator ativado para o administrador {UsuarioId}", usuario.Id);
        return Resultado.Ok();
    }

    /// <summary>Usado pelo cookie a cada poucos minutos: sessão continua válida?</summary>
    public async Task<bool> SessaoValidaAsync(int usuarioId, Guid carimbo, CancellationToken ct = default) =>
        await _db.Usuarios.AsNoTracking().AnyAsync(u => u.Id == usuarioId && u.CarimboSeguranca == carimbo && !u.Anonimizado, ct);

    // ================================================================== senha (RF06)

    /// <summary>Sempre "sucesso" para quem chama: não revela se o e-mail tem conta.</summary>
    public async Task SolicitarRedefinicaoSenhaAsync(string email, CancellationToken ct = default)
    {
        var normalizado = NormalizarEmail(email);
        var usuario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Email == normalizado && !u.Anonimizado, ct);
        if (usuario is null) return;

        var recentes = await _db.TokensUsuario.CountAsync(t => t.UsuarioId == usuario.Id && t.Tipo == TipoToken.RedefinicaoSenha
            && t.CriadoEm > _relogio.AgoraUtc.AddHours(-1), ct);
        if (recentes >= 3) return;   // limita o envio de e-mails para a mesma conta

        var token = await CriarTokenAsync(usuario.Id, TipoToken.RedefinicaoSenha, ValidadeRedefinicaoSenha, ct);
        var msg = ModelosEmail.RedefinicaoSenha(usuario.Nome, _links.RedefinirSenha(token));
        await _email.EnviarAsync(usuario.Email, msg.Assunto, msg.Html, ct);
    }

    public async Task<bool> TokenRedefinicaoValidoAsync(string token, CancellationToken ct = default) =>
        await ObterTokenValidoAsync(token, TipoToken.RedefinicaoSenha, ct) is not null;

    public async Task<Resultado> RedefinirSenhaAsync(string token, string novaSenha, CancellationToken ct = default)
    {
        var erro = ValidarSenha(novaSenha);
        if (erro is not null) return Resultado.Falha(erro);

        var registro = await ObterTokenValidoAsync(token, TipoToken.RedefinicaoSenha, ct);
        if (registro?.Usuario is null) return Resultado.Falha("Link inválido ou expirado. Peça uma nova redefinição.");

        var agora = _relogio.AgoraUtc;
        var usuario = registro.Usuario;
        registro.UsadoEm = agora;
        usuario.SenhaHash = _hash.Gerar(novaSenha);
        usuario.InvalidarSessoes();
        usuario.FalhasLogin = 0;
        usuario.BloqueadoAte = null;
        usuario.EmailConfirmado = true;   // quem recebeu o link prova que é dono do e-mail
        usuario.AlteradoEm = agora;
        await _db.SaveChangesAsync(ct);

        var msg = ModelosEmail.SenhaAlterada(usuario.Nome);
        await _email.EnviarAsync(usuario.Email, msg.Assunto, msg.Html, ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> AlterarSenhaAsync(int usuarioId, string senhaAtual, string novaSenha, CancellationToken ct = default)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null) return Resultado.Falha("Conta não encontrada.");
        if (_hash.Verificar(usuario.SenhaHash, senhaAtual ?? string.Empty) == VerificacaoSenha.Falha)
            return Resultado.Falha("Senha atual incorreta.");
        var erro = ValidarSenha(novaSenha);
        if (erro is not null) return Resultado.Falha(erro);

        usuario.SenhaHash = _hash.Gerar(novaSenha);
        usuario.InvalidarSessoes();
        usuario.AlteradoEm = _relogio.AgoraUtc;
        await _db.SaveChangesAsync(ct);

        var msg = ModelosEmail.SenhaAlterada(usuario.Nome);
        await _email.EnviarAsync(usuario.Email, msg.Assunto, msg.Html, ct);
        return Resultado.Ok();
    }

    // ================================================================== dados pessoais e LGPD (RNF07)

    public Task<Usuario?> ObterAsync(int usuarioId, CancellationToken ct = default) =>
        _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId && !u.Anonimizado, ct);

    public async Task<Resultado> AtualizarDadosAsync(int usuarioId, string nome, string telefone, CancellationToken ct = default)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId && !u.Anonimizado, ct);
        if (usuario is null) return Resultado.Falha("Conta não encontrada.");
        var nomeLimpo = Texto.Limpar(nome) ?? string.Empty;
        if (nomeLimpo.Length < 3 || !nomeLimpo.Contains(' ') || nomeLimpo.Length > 150) return Resultado.Falha("Informe nome e sobrenome.");
        var tel = Telefone.Normalizar(telefone);
        if (!Telefone.EhValido(tel)) return Resultado.Falha("Telefone inválido.");

        usuario.Nome = nomeLimpo;
        usuario.Telefone = tel;
        usuario.AlteradoEm = _relogio.AgoraUtc;
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>LGPD, art. 18: cópia dos dados pessoais em formato legível por máquina.</summary>
    public async Task<object?> ExportarDadosAsync(int usuarioId, CancellationToken ct = default)
    {
        var u = await _db.Usuarios.AsNoTracking().Include(x => x.Enderecos).FirstOrDefaultAsync(x => x.Id == usuarioId && !x.Anonimizado, ct);
        if (u is null) return null;

        var pedidos = await _db.Pedidos.AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .Include(p => p.Itens)
            .OrderBy(p => p.CriadoEm)
            .ToListAsync(ct);

        return new
        {
            geradoEm = _relogio.AgoraUtc,
            conta = new { u.Nome, u.Email, u.EmailConfirmado, Cpf = Cpf.Formatar(u.Cpf), Telefone = Telefone.Formatar(u.Telefone), u.CriadoEm, u.AceitouTermosEm, u.UltimoLoginEm },
            enderecos = u.Enderecos.Select(e => new { e.Apelido, e.Destinatario, e.Cep, e.Logradouro, e.Numero, e.Complemento, e.Bairro, e.Cidade, e.Uf }),
            pedidos = pedidos.Select(p => new
            {
                numero = p.Numero,
                p.CriadoEm,
                status = p.Status.ToString(),
                p.Subtotal,
                p.ValorFrete,
                p.Total,
                entrega = p.Entrega,
                itens = p.Itens.Select(i => new { i.NomeProduto, i.Quantidade, i.PrecoUnitario, i.Total })
            })
        };
    }

    /// <summary>LGPD: exclusão a pedido do titular, por anonimização (os pedidos são guardados por obrigação fiscal).</summary>
    public async Task<Resultado> AnonimizarAsync(int usuarioId, string senha, CancellationToken ct = default)
    {
        var usuario = await _db.Usuarios.Include(u => u.Enderecos).FirstOrDefaultAsync(u => u.Id == usuarioId && !u.Anonimizado, ct);
        if (usuario is null) return Resultado.Falha("Conta não encontrada.");
        if (usuario.EhAdministrador) return Resultado.Falha("Contas de administrador não podem ser excluídas por aqui.");
        if (_hash.Verificar(usuario.SenhaHash, senha ?? string.Empty) == VerificacaoSenha.Falha)
            return Resultado.Falha("Senha incorreta.");

        var emAndamento = await _db.Pedidos.AnyAsync(p => p.UsuarioId == usuarioId &&
            (p.Status == Domain.Pedidos.StatusPedido.AguardandoPagamento || p.Status == Domain.Pedidos.StatusPedido.PagamentoEmAnalise
             || p.Status == Domain.Pedidos.StatusPedido.Pago || p.Status == Domain.Pedidos.StatusPedido.EmSeparacao
             || p.Status == Domain.Pedidos.StatusPedido.Enviado), ct);
        if (emAndamento)
            return Resultado.Falha("Você tem pedidos em andamento. A exclusão fica disponível quando todos forem entregues ou cancelados.");

        _db.Enderecos.RemoveRange(usuario.Enderecos);
        await _db.TokensUsuario.Where(t => t.UsuarioId == usuarioId).ExecuteDeleteAsync(ct);
        await _db.Carrinhos.Where(c => c.UsuarioId == usuarioId).ExecuteDeleteAsync(ct);
        usuario.Anonimizar(_relogio.AgoraUtc);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Conta {UsuarioId} anonimizada a pedido do titular", usuarioId);
        return Resultado.Ok();
    }

    // ================================================================== administração

    public async Task<Resultado> CriarAdministradorAsync(string email, string nome, string senha, CancellationToken ct = default)
    {
        var normalizado = NormalizarEmail(email);
        if (!EmailValido(normalizado)) return Resultado.Falha("E-mail inválido.");
        var erro = ValidarSenha(senha);
        if (erro is not null) return Resultado.Falha(erro);
        if (senha.Length < 12) return Resultado.Falha("Para administradores, use pelo menos 12 caracteres.");
        if (await _db.Usuarios.AnyAsync(u => u.Email == normalizado, ct)) return Resultado.Falha("Já existe uma conta com este e-mail.");

        var agora = _relogio.AgoraUtc;
        _db.Usuarios.Add(new Usuario
        {
            Nome = Texto.Limpar(nome) ?? "Administrador",
            Email = normalizado,
            EmailConfirmado = true,
            Perfil = PerfilUsuario.Administrador,
            SenhaHash = _hash.Gerar(senha),
            AceitouTermosEm = agora,
            CriadoEm = agora
        });
        await _db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    // ================================================================== acesso pelo WhatsApp

    public static readonly TimeSpan ValidadeAcessoWhatsApp = TimeSpan.FromHours(24);

    /// <summary>Marca no SenhaHash das contas criadas na conversa do WhatsApp (sem senha: login impossível).</summary>
    public const string MarcadorContaWhatsApp = "!whatsapp";

    /// <summary>Link de acesso enviado no WhatsApp para o cliente pagar com cartão ou boleto no site. Vale 24 h.</summary>
    public Task<string> CriarAcessoWhatsAppAsync(int usuarioId, CancellationToken ct = default) =>
        CriarTokenAsync(usuarioId, TipoToken.AcessoWhatsApp, ValidadeAcessoWhatsApp, ct);

    /// <summary>Confere o link de acesso. Nunca libera conta de administrador.</summary>
    public async Task<Usuario?> ValidarAcessoWhatsAppAsync(string? token, CancellationToken ct = default)
    {
        var registro = await ObterTokenValidoAsync(token, TipoToken.AcessoWhatsApp, ct);
        return registro?.Usuario is { EhAdministrador: false } usuario ? usuario : null;
    }

    // ================================================================== tokens

    private async Task<string> CriarTokenAsync(int usuarioId, TipoToken tipo, TimeSpan validade, CancellationToken ct)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var agora = _relogio.AgoraUtc;

        // Um token ativo por tipo: os anteriores deixam de valer
        await _db.TokensUsuario
            .Where(t => t.UsuarioId == usuarioId && t.Tipo == tipo && t.UsadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, (DateTime?)agora), ct);

        _db.TokensUsuario.Add(new TokenUsuario
        {
            UsuarioId = usuarioId,
            Tipo = tipo,
            TokenHash = HashToken(token),
            ExpiraEm = agora.Add(validade),
            CriadoEm = agora
        });
        await _db.SaveChangesAsync(ct);
        return token;
    }

    private async Task<TokenUsuario?> ObterTokenValidoAsync(string? token, TipoToken tipo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = HashToken(token);
        var registro = await _db.TokensUsuario.Include(t => t.Usuario)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Tipo == tipo, ct);
        return registro is not null && registro.EstaValido(_relogio.AgoraUtc) && registro.Usuario is { Anonimizado: false }
            ? registro
            : null;
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
