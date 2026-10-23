using System.Globalization;
using System.Security.Cryptography;
using PortalVendas.Application.Licenciamento;
using PortalVendas.Domain.Comum;
using PortalVendas.Infrastructure.Licenciamento;

// Licenciador do PortalVendas — uso exclusivo do fornecedor.
//
//   licenciador chaves    --saida C:\Licencas\chave-privada.pem
//   licenciador emitir    --chave C:\Licencas\chave-privada.pem --nome "Prado Shop" --razao "Prado Shop Comércio Ltda."
//                         --cnpj 12.345.678/0001-95 --dominio pradoshop.com.br --dominio www.pradoshop.com.br
//                         [--expira 2027-12-31] [--atualizacoes-ate 2027-12-31] --saida licenca.lic
//   licenciador verificar --arquivo licenca.lic [--publica <base64>]
//
// A chave privada NUNCA vai para o repositório nem para o cliente: guarde-a fora da pasta do projeto, com backup.
// Sem ela não dá para emitir novas licenças; se ela vazar, qualquer um emite licenças (gere outro par e publique nova versão).

return Executar(args);

static int Executar(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "ajuda")
    {
        Ajuda();
        return 0;
    }

    var opcoes = LerOpcoes(args.Skip(1).ToArray());
    try
    {
        return args[0] switch
        {
            "chaves" => GerarChaves(opcoes),
            "emitir" => Emitir(opcoes),
            "verificar" => Verificar(opcoes),
            _ => Erro($"Comando desconhecido: {args[0]}")
        };
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
    {
        return Erro(ex.Message);
    }
}

static int GerarChaves(Dictionary<string, List<string>> o)
{
    var saida = Um(o, "saida") ?? throw new ArgumentException("Informe --saida <arquivo .pem da chave privada>.");
    if (File.Exists(saida)) return Erro($"{saida} já existe. Não sobrescrevo uma chave privada — escolha outro caminho.");

    using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(saida))!);
    File.WriteAllText(saida, ecdsa.ExportPkcs8PrivateKeyPem());
    var publica = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    File.WriteAllText(Path.ChangeExtension(saida, ".publica.txt"), publica);

    Console.WriteLine($"Chave privada gravada em {Path.GetFullPath(saida)} (guarde com backup e NÃO coloque no Git).");
    Console.WriteLine();
    Console.WriteLine("Chave pública — cole em src/PortalVendas.Infrastructure/Licenciamento/ChavePublicaLicenca.cs:");
    Console.WriteLine();
    Console.WriteLine($"    public const string Valor = \"{publica}\";");
    Console.WriteLine();
    Console.WriteLine("Depois recompile e publique o site. Todas as licenças passam a ser conferidas com esta chave.");
    return 0;
}

static int Emitir(Dictionary<string, List<string>> o)
{
    var caminhoChave = Um(o, "chave") ?? throw new ArgumentException("Informe --chave <chave-privada.pem>.");
    var nome = Um(o, "nome") ?? throw new ArgumentException("Informe --nome \"Nome da loja\".");
    var razao = Um(o, "razao") ?? throw new ArgumentException("Informe --razao \"Razão social\".");
    var cnpj = Cnpj.Normalizar(Um(o, "cnpj") ?? "");
    if (!Cnpj.EhValido(cnpj)) return Erro("CNPJ inválido (confira os dígitos verificadores).");
    var dominios = (o.GetValueOrDefault("dominio") ?? new List<string>())
        .Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length > 0).Distinct().ToList();
    if (dominios.Count == 0) return Erro("Informe ao menos um --dominio (ex.: --dominio minhaloja.com.br --dominio www.minhaloja.com.br).");
    if (dominios.Any(d => d.Contains('/') || d.Contains(':')))
        return Erro("Domínio é só o nome (ex.: www.minhaloja.com.br), sem https:// nem porta.");
    var saida = Um(o, "saida") ?? "licenca.lic";

    var dados = new DadosLicenca
    {
        Id = Guid.NewGuid(),
        Produto = ArquivoLicenca.Produto,
        NomeLoja = nome.Trim(),
        RazaoSocial = razao.Trim(),
        Cnpj = cnpj,
        Dominios = dominios,
        EmitidaEm = DateTime.UtcNow,
        ExpiraEm = Data(Um(o, "expira")),
        AtualizacoesAte = Data(Um(o, "atualizacoes-ate"))
    };

    using var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(File.ReadAllText(caminhoChave));
    File.WriteAllBytes(saida, ArquivoLicenca.Emitir(dados, ecdsa));

    Console.WriteLine($"Licença {dados.Id:N} gravada em {Path.GetFullPath(saida)}");
    Console.WriteLine(ArquivoLicenca.Descrever(dados));
    Console.WriteLine(dados.ExpiraEm is null ? "Sem vencimento." : $"Vence em {dados.ExpiraEm:dd/MM/yyyy}.");
    Console.WriteLine("Envie o arquivo ao cliente: ele instala em Painel → Identidade da loja → Instalar licença.");
    return 0;
}

static int Verificar(Dictionary<string, List<string>> o)
{
    var arquivo = Um(o, "arquivo") ?? throw new ArgumentException("Informe --arquivo licenca.lic.");
    var publica = Um(o, "publica") ?? ChavePublica();
    var (dados, erro) = ArquivoLicenca.Ler(File.ReadAllBytes(arquivo), publica);
    if (dados is null) return Erro(erro!);
    Console.WriteLine("Licença válida.");
    Console.WriteLine(ArquivoLicenca.Descrever(dados));
    Console.WriteLine($"Emitida em {dados.EmitidaEm:dd/MM/yyyy}; " + (dados.ExpiraEm is null ? "sem vencimento." : $"vence em {dados.ExpiraEm:dd/MM/yyyy}."));
    return 0;
}

static string ChavePublica()
{
    // Mesma chave embutida no site (classe internal: lida por reflexão para não abrir o tipo)
    var tipo = typeof(ArquivoLicenca).Assembly.GetType("PortalVendas.Infrastructure.Licenciamento.ChavePublicaLicenca");
    return tipo?.GetField("Valor")?.GetValue(null) as string ?? "";
}

static DateTime? Data(string? texto) =>
    string.IsNullOrWhiteSpace(texto) ? null
    : DateTime.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
        ? d.Date.AddDays(1).AddTicks(-1)   // vale até o fim do dia informado
        : throw new ArgumentException($"Data inválida: {texto} (use aaaa-mm-dd).");

static Dictionary<string, List<string>> LerOpcoes(string[] args)
{
    var r = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Argumento inesperado: {args[i]}");
        var chave = args[i][2..];
        var valor = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "";
        if (!r.TryGetValue(chave, out var lista)) r[chave] = lista = new List<string>();
        lista.Add(valor);
    }
    return r;
}

static string? Um(Dictionary<string, List<string>> o, string chave) =>
    o.TryGetValue(chave, out var l) && l.Count > 0 && !string.IsNullOrWhiteSpace(l[^1]) ? l[^1] : null;

static int Erro(string mensagem)
{
    Console.Error.WriteLine("Erro: " + mensagem);
    return 1;
}

static void Ajuda() => Console.WriteLine("""
    Licenciador do PortalVendas (uso do fornecedor)

      chaves     --saida <chave-privada.pem>        gera o par de chaves (uma vez só)
      emitir     --chave <pem> --nome <nome> --razao <razão social> --cnpj <cnpj>
                 --dominio <dominio> [--dominio ...] [--expira aaaa-mm-dd]
                 [--atualizacoes-ate aaaa-mm-dd] [--saida licenca.lic]
      verificar  --arquivo licenca.lic [--publica <base64>]

    Domínio aceita curinga: --dominio *.minhaloja.com.br
    """);
