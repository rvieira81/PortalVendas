/* =====================================================================
   PortalVendas — V001 — Criação inicial do esquema
   Convenções:
     - Tabelas no singular, PascalCase, esquema dbo
     - PK "Id" (INT IDENTITY; BIGINT nas tabelas de log)
     - FK "<Tabela>Id"
     - Datas sempre em UTC (datetime2(3)); a aplicação converte para America/Sao_Paulo
     - Valores monetários decimal(12,2)
     - Nomes de constraints: PK_, FK_, UQ_, CK_, DF_, IX_
   ===================================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;

/* ---------------------------------------------------------------------
   Catálogo
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Categoria (
    Id          INT IDENTITY(1,1) NOT NULL,
    Nome        NVARCHAR(100)     NOT NULL,
    Slug        VARCHAR(120)      NOT NULL,
    Ativa       BIT               NOT NULL CONSTRAINT DF_Categoria_Ativa DEFAULT (1),
    CriadoEm    DATETIME2(3)      NOT NULL CONSTRAINT DF_Categoria_CriadoEm DEFAULT (SYSUTCDATETIME()),
    AlteradoEm  DATETIME2(3)      NULL,
    CONSTRAINT PK_Categoria PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Categoria_Nome UNIQUE (Nome),
    CONSTRAINT UQ_Categoria_Slug UNIQUE (Slug)
);

CREATE TABLE dbo.Produto (
    Id                INT IDENTITY(1,1) NOT NULL,
    CategoriaId       INT               NOT NULL,
    Sku               VARCHAR(50)       NOT NULL,
    -- Collation sem diferenciar maiúsculas e acentos: "cafe" encontra "Café"
    Nome              NVARCHAR(200)  COLLATE Latin1_General_100_CI_AI NOT NULL,
    Slug              VARCHAR(220)      NOT NULL,
    Descricao         NVARCHAR(4000) COLLATE Latin1_General_100_CI_AI NOT NULL CONSTRAINT DF_Produto_Descricao DEFAULT (N''),
    Preco             DECIMAL(12,2)     NOT NULL,
    PrecoDe           DECIMAL(12,2)     NULL,     -- preço "de" riscado na vitrine
    Estoque           INT               NOT NULL CONSTRAINT DF_Produto_Estoque DEFAULT (0),
    EstoqueReservado  INT               NOT NULL CONSTRAINT DF_Produto_EstoqueReservado DEFAULT (0),
    PesoKg            DECIMAL(8,3)      NOT NULL,
    AlturaCm          DECIMAL(6,1)      NOT NULL,
    LarguraCm         DECIMAL(6,1)      NOT NULL,
    ComprimentoCm     DECIMAL(6,1)      NOT NULL,
    Ativo             BIT               NOT NULL CONSTRAINT DF_Produto_Ativo DEFAULT (1),
    CriadoEm          DATETIME2(3)      NOT NULL CONSTRAINT DF_Produto_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CriadoPor         NVARCHAR(100)     NOT NULL,
    AlteradoEm        DATETIME2(3)      NULL,
    AlteradoPor       NVARCHAR(100)     NULL,
    CONSTRAINT PK_Produto PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Produto_Categoria FOREIGN KEY (CategoriaId) REFERENCES dbo.Categoria (Id),
    CONSTRAINT UQ_Produto_Sku  UNIQUE (Sku),
    CONSTRAINT UQ_Produto_Slug UNIQUE (Slug),
    CONSTRAINT CK_Produto_Preco            CHECK (Preco > 0),
    CONSTRAINT CK_Produto_PrecoDe          CHECK (PrecoDe IS NULL OR PrecoDe > Preco),
    CONSTRAINT CK_Produto_Estoque          CHECK (Estoque >= 0),
    CONSTRAINT CK_Produto_EstoqueReservado CHECK (EstoqueReservado >= 0 AND EstoqueReservado <= Estoque),  -- RN03
    CONSTRAINT CK_Produto_Dimensoes        CHECK (PesoKg > 0 AND AlturaCm > 0 AND LarguraCm > 0 AND ComprimentoCm > 0)
);

-- Vitrine: WHERE Ativo = 1 [AND CategoriaId = @c] [AND Preco BETWEEN ...] ORDER BY Preco/Nome
CREATE NONCLUSTERED INDEX IX_Produto_Vitrine
    ON dbo.Produto (Ativo, CategoriaId, Preco)
    INCLUDE (Nome, Slug, PrecoDe, Estoque, EstoqueReservado, CriadoEm);
-- Vitrine sem filtro de categoria, ordenada por "mais recentes"
CREATE NONCLUSTERED INDEX IX_Produto_Ativo_CriadoEm
    ON dbo.Produto (Ativo, CriadoEm DESC)
    INCLUDE (Nome, Slug, Preco, PrecoDe, Estoque, EstoqueReservado, CategoriaId);

CREATE TABLE dbo.ProdutoImagem (
    Id         INT IDENTITY(1,1) NOT NULL,
    ProdutoId  INT               NOT NULL,
    Arquivo    VARCHAR(100)      NOT NULL,   -- nome gerado (GUID + extensão), nunca o nome enviado
    Ordem      INT               NOT NULL CONSTRAINT DF_ProdutoImagem_Ordem DEFAULT (0),
    CONSTRAINT PK_ProdutoImagem PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ProdutoImagem_Produto FOREIGN KEY (ProdutoId) REFERENCES dbo.Produto (Id) ON DELETE CASCADE
);
CREATE NONCLUSTERED INDEX IX_ProdutoImagem_ProdutoId ON dbo.ProdutoImagem (ProdutoId, Ordem) INCLUDE (Arquivo);

/* ---------------------------------------------------------------------
   Usuários (clientes e administradores)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Usuario (
    Id                    INT IDENTITY(1,1) NOT NULL,
    Nome                  NVARCHAR(150)     NOT NULL,
    Email                 VARCHAR(254)      NOT NULL,     -- gravado sempre em minúsculas
    EmailConfirmado       BIT               NOT NULL CONSTRAINT DF_Usuario_EmailConfirmado DEFAULT (0),
    Cpf                   CHAR(11)          NULL,         -- LGPD: dado pessoal; mascarado em telas e logs
    Telefone              VARCHAR(11)       NULL,         -- LGPD: dado pessoal
    Perfil                TINYINT           NOT NULL,     -- 1 Cliente, 2 Administrador
    SenhaHash             VARCHAR(256)      NOT NULL,     -- PBKDF2 (PasswordHasher do ASP.NET Core)
    CarimboSeguranca      UNIQUEIDENTIFIER  NOT NULL,     -- muda ao trocar senha: invalida sessões
    FalhasLogin           INT               NOT NULL CONSTRAINT DF_Usuario_FalhasLogin DEFAULT (0),
    BloqueadoAte          DATETIME2(3)      NULL,
    TotpSegredoProtegido  VARCHAR(500)      NULL,         -- cifrado pelo Data Protection
    TotpAtivo             BIT               NOT NULL CONSTRAINT DF_Usuario_TotpAtivo DEFAULT (0),
    AceitouTermosEm       DATETIME2(3)      NULL,
    Anonimizado           BIT               NOT NULL CONSTRAINT DF_Usuario_Anonimizado DEFAULT (0),
    UltimoLoginEm         DATETIME2(3)      NULL,
    CriadoEm              DATETIME2(3)      NOT NULL CONSTRAINT DF_Usuario_CriadoEm DEFAULT (SYSUTCDATETIME()),
    AlteradoEm            DATETIME2(3)      NULL,
    CONSTRAINT PK_Usuario PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Usuario_Email UNIQUE (Email),
    CONSTRAINT CK_Usuario_Perfil CHECK (Perfil IN (1, 2)),
    CONSTRAINT CK_Usuario_Cpf CHECK (Cpf IS NULL OR Cpf NOT LIKE '%[^0-9]%')
);
-- CPF único só entre quem tem CPF (administradores e contas anonimizadas ficam sem)
CREATE UNIQUE NONCLUSTERED INDEX UQ_Usuario_Cpf ON dbo.Usuario (Cpf) WHERE Cpf IS NOT NULL;

CREATE TABLE dbo.TokenUsuario (
    Id         INT IDENTITY(1,1) NOT NULL,
    UsuarioId  INT               NOT NULL,
    Tipo       TINYINT           NOT NULL,      -- 1 Confirmação de e-mail, 2 Redefinição de senha
    TokenHash  CHAR(64)          NOT NULL,      -- SHA-256 em hexadecimal; o token em si nunca é gravado
    ExpiraEm   DATETIME2(3)      NOT NULL,
    UsadoEm    DATETIME2(3)      NULL,
    CriadoEm   DATETIME2(3)      NOT NULL CONSTRAINT DF_TokenUsuario_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_TokenUsuario PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_TokenUsuario_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_TokenUsuario_TokenHash UNIQUE (TokenHash),
    CONSTRAINT CK_TokenUsuario_Tipo CHECK (Tipo IN (1, 2))
);
CREATE NONCLUSTERED INDEX IX_TokenUsuario_UsuarioId ON dbo.TokenUsuario (UsuarioId, Tipo);

CREATE TABLE dbo.Endereco (
    Id            INT IDENTITY(1,1) NOT NULL,
    UsuarioId     INT               NOT NULL,
    Apelido       NVARCHAR(50)      NOT NULL,   -- "Casa", "Trabalho"
    Destinatario  NVARCHAR(150)     NOT NULL,
    Cep           CHAR(8)           NOT NULL,
    Logradouro    NVARCHAR(200)     NOT NULL,
    Numero        NVARCHAR(20)      NOT NULL,
    Complemento   NVARCHAR(100)     NULL,
    Bairro        NVARCHAR(100)     NOT NULL,
    Cidade        NVARCHAR(100)     NOT NULL,
    Uf            CHAR(2)           NOT NULL,
    Padrao        BIT               NOT NULL CONSTRAINT DF_Endereco_Padrao DEFAULT (0),
    CriadoEm      DATETIME2(3)      NOT NULL CONSTRAINT DF_Endereco_CriadoEm DEFAULT (SYSUTCDATETIME()),
    AlteradoEm    DATETIME2(3)      NULL,
    CONSTRAINT PK_Endereco PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Endereco_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id) ON DELETE CASCADE,
    CONSTRAINT CK_Endereco_Cep CHECK (Cep NOT LIKE '%[^0-9]%' AND LEN(Cep) = 8),
    CONSTRAINT CK_Endereco_Uf  CHECK (Uf IN ('AC','AL','AP','AM','BA','CE','DF','ES','GO','MA','MT','MS','MG','PA',
                                              'PB','PR','PE','PI','RJ','RN','RS','RO','RR','SC','SP','SE','TO'))
);
CREATE NONCLUSTERED INDEX IX_Endereco_UsuarioId ON dbo.Endereco (UsuarioId);

/* ---------------------------------------------------------------------
   Carrinho
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Carrinho (
    Id           INT IDENTITY(1,1) NOT NULL,
    Token        UNIQUEIDENTIFIER  NOT NULL,   -- valor do cookie do carrinho anônimo
    UsuarioId    INT               NULL,
    CriadoEm     DATETIME2(3)      NOT NULL CONSTRAINT DF_Carrinho_CriadoEm DEFAULT (SYSUTCDATETIME()),
    AtualizadoEm DATETIME2(3)      NOT NULL CONSTRAINT DF_Carrinho_AtualizadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Carrinho PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Carrinho_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_Carrinho_Token UNIQUE (Token)
);
CREATE UNIQUE NONCLUSTERED INDEX UQ_Carrinho_UsuarioId ON dbo.Carrinho (UsuarioId) WHERE UsuarioId IS NOT NULL;
-- Limpeza de carrinhos anônimos abandonados
CREATE NONCLUSTERED INDEX IX_Carrinho_AtualizadoEm ON dbo.Carrinho (AtualizadoEm) WHERE UsuarioId IS NULL;

CREATE TABLE dbo.CarrinhoItem (
    Id               INT IDENTITY(1,1) NOT NULL,
    CarrinhoId       INT               NOT NULL,
    ProdutoId        INT               NOT NULL,
    Quantidade       INT               NOT NULL,
    PrecoNaInclusao  DECIMAL(12,2)     NOT NULL,  -- para avisar o cliente quando o preço mudar
    CONSTRAINT PK_CarrinhoItem PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_CarrinhoItem_Carrinho FOREIGN KEY (CarrinhoId) REFERENCES dbo.Carrinho (Id) ON DELETE CASCADE,
    CONSTRAINT FK_CarrinhoItem_Produto  FOREIGN KEY (ProdutoId)  REFERENCES dbo.Produto (Id),
    CONSTRAINT UQ_CarrinhoItem_Carrinho_Produto UNIQUE (CarrinhoId, ProdutoId),
    CONSTRAINT CK_CarrinhoItem_Quantidade CHECK (Quantidade BETWEEN 1 AND 10)   -- RN09
);
CREATE NONCLUSTERED INDEX IX_CarrinhoItem_ProdutoId ON dbo.CarrinhoItem (ProdutoId);

/* ---------------------------------------------------------------------
   Pedidos
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Pedido (
    Id                   INT IDENTITY(1,1) NOT NULL,
    UsuarioId            INT               NOT NULL,
    Status               TINYINT           NOT NULL,
    ChaveIdempotencia    UNIQUEIDENTIFIER  NOT NULL,     -- evita pedido duplicado no duplo clique (RF09)
    Subtotal             DECIMAL(12,2)     NOT NULL,
    ValorFrete           DECIMAL(12,2)     NOT NULL,
    Desconto             DECIMAL(12,2)     NOT NULL CONSTRAINT DF_Pedido_Desconto DEFAULT (0),
    Total                DECIMAL(12,2)     NOT NULL,
    FreteServicoId       INT               NULL,         -- Id do serviço no Melhor Envio; NULL = contingência
    FreteDescricao       NVARCHAR(150)     NOT NULL,     -- "Correios — SEDEX"
    FretePrazoDias       INT               NOT NULL,
    -- Endereço de entrega copiado no momento da compra (o cliente pode alterar/excluir o endereço depois)
    EntregaDestinatario  NVARCHAR(150)     NOT NULL,
    EntregaCep           CHAR(8)           NOT NULL,
    EntregaLogradouro    NVARCHAR(200)     NOT NULL,
    EntregaNumero        NVARCHAR(20)      NOT NULL,
    EntregaComplemento   NVARCHAR(100)     NULL,
    EntregaBairro        NVARCHAR(100)     NOT NULL,
    EntregaCidade        NVARCHAR(100)     NOT NULL,
    EntregaUf            CHAR(2)           NOT NULL,
    CodigoRastreio       VARCHAR(50)       NULL,
    ExpiraEm             DATETIME2(3)      NULL,         -- prazo para pagar (Pix 30 min / boleto)
    PagoEm               DATETIME2(3)      NULL,
    EnviadoEm            DATETIME2(3)      NULL,
    EntregueEm           DATETIME2(3)      NULL,
    CanceladoEm          DATETIME2(3)      NULL,
    MotivoCancelamento   NVARCHAR(300)     NULL,
    CriadoEm             DATETIME2(3)      NOT NULL CONSTRAINT DF_Pedido_CriadoEm DEFAULT (SYSUTCDATETIME()),
    AlteradoEm           DATETIME2(3)      NULL,
    Versao               INT               NOT NULL CONSTRAINT DF_Pedido_Versao DEFAULT (0),  -- concorrência otimista
    CONSTRAINT PK_Pedido PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Pedido_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT UQ_Pedido_ChaveIdempotencia UNIQUE (ChaveIdempotencia),
    -- 1 AguardandoPagamento, 2 PagamentoEmAnalise, 3 Pago, 4 EmSeparacao, 5 Enviado, 6 Entregue, 9 Cancelado
    CONSTRAINT CK_Pedido_Status  CHECK (Status IN (1, 2, 3, 4, 5, 6, 9)),
    CONSTRAINT CK_Pedido_Valores CHECK (Subtotal > 0 AND ValorFrete >= 0 AND Desconto >= 0 AND Total >= 0),
    CONSTRAINT CK_Pedido_Total   CHECK (Total = Subtotal + ValorFrete - Desconto),   -- RN05
    CONSTRAINT CK_Pedido_EntregaCep CHECK (EntregaCep NOT LIKE '%[^0-9]%' AND LEN(EntregaCep) = 8)
);
-- Meus pedidos (cliente)
CREATE NONCLUSTERED INDEX IX_Pedido_UsuarioId_CriadoEm ON dbo.Pedido (UsuarioId, CriadoEm DESC) INCLUDE (Status, Total);
-- Admin: filtro por status e período
CREATE NONCLUSTERED INDEX IX_Pedido_Status_CriadoEm ON dbo.Pedido (Status, CriadoEm DESC) INCLUDE (UsuarioId, Total);
-- Admin: filtro só por período e painel de vendas
CREATE NONCLUSTERED INDEX IX_Pedido_CriadoEm ON dbo.Pedido (CriadoEm) INCLUDE (Status, Total);
-- Job de expiração
CREATE NONCLUSTERED INDEX IX_Pedido_Status_ExpiraEm ON dbo.Pedido (Status, ExpiraEm);

CREATE TABLE dbo.PedidoItem (
    Id             INT IDENTITY(1,1) NOT NULL,
    PedidoId       INT               NOT NULL,
    ProdutoId      INT               NOT NULL,
    NomeProduto    NVARCHAR(200)     NOT NULL,   -- cópia: o produto pode ser renomeado depois
    Sku            VARCHAR(50)       NOT NULL,
    PrecoUnitario  DECIMAL(12,2)     NOT NULL,   -- RN01: preço congelado na compra
    Quantidade     INT               NOT NULL,
    Total          DECIMAL(12,2)     NOT NULL,
    CONSTRAINT PK_PedidoItem PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_PedidoItem_Pedido  FOREIGN KEY (PedidoId)  REFERENCES dbo.Pedido (Id) ON DELETE CASCADE,
    CONSTRAINT FK_PedidoItem_Produto FOREIGN KEY (ProdutoId) REFERENCES dbo.Produto (Id),
    CONSTRAINT CK_PedidoItem_Quantidade CHECK (Quantidade > 0),
    CONSTRAINT CK_PedidoItem_Total CHECK (Total = PrecoUnitario * Quantidade)
);
CREATE NONCLUSTERED INDEX IX_PedidoItem_PedidoId  ON dbo.PedidoItem (PedidoId);
CREATE NONCLUSTERED INDEX IX_PedidoItem_ProdutoId ON dbo.PedidoItem (ProdutoId) INCLUDE (Quantidade, Total);

CREATE TABLE dbo.PedidoHistorico (
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    PedidoId        INT                  NOT NULL,
    StatusAnterior  TINYINT              NULL,
    StatusNovo      TINYINT              NOT NULL,
    Observacao      NVARCHAR(300)        NULL,
    Autor           NVARCHAR(100)        NOT NULL,   -- e-mail do admin, "cliente:{id}", "sistema" ou "mercadopago"
    CriadoEm        DATETIME2(3)         NOT NULL CONSTRAINT DF_PedidoHistorico_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_PedidoHistorico PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_PedidoHistorico_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id) ON DELETE CASCADE
);
CREATE NONCLUSTERED INDEX IX_PedidoHistorico_PedidoId ON dbo.PedidoHistorico (PedidoId, CriadoEm);

/* ---------------------------------------------------------------------
   Pagamentos
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Pagamento (
    Id                    INT IDENTITY(1,1) NOT NULL,
    PedidoId              INT               NOT NULL,
    Metodo                TINYINT           NOT NULL,   -- 1 Pix, 2 Cartão de crédito, 3 Boleto
    -- 1 Pendente, 2 EmAnalise, 3 Aprovado, 4 Recusado, 5 Cancelado, 6 Estornado, 7 Contestado (chargeback)
    Status                TINYINT           NOT NULL,
    StatusGateway         VARCHAR(30)       NULL,       -- status original do Mercado Pago
    StatusDetalhe         VARCHAR(100)      NULL,       -- status_detail (motivo da recusa)
    GatewayId             BIGINT            NULL,       -- id do pagamento no Mercado Pago
    ChaveIdempotencia     UNIQUEIDENTIFIER  NOT NULL,   -- enviado em X-Idempotency-Key
    Valor                 DECIMAL(12,2)     NOT NULL,
    Parcelas              INT               NULL,
    Bandeira              VARCHAR(30)       NULL,
    UltimosDigitos        CHAR(4)           NULL,       -- só os 4 últimos dígitos, nunca o número
    PixQrCode             VARCHAR(1000)     NULL,       -- código copia-e-cola
    PixQrCodeBase64       VARCHAR(MAX)      NULL,       -- imagem do QR Code
    BoletoUrl             VARCHAR(500)      NULL,
    BoletoLinhaDigitavel  VARCHAR(60)       NULL,
    ExpiraEm              DATETIME2(3)      NULL,
    CriadoEm              DATETIME2(3)      NOT NULL CONSTRAINT DF_Pagamento_CriadoEm DEFAULT (SYSUTCDATETIME()),
    AlteradoEm            DATETIME2(3)      NULL,
    CONSTRAINT PK_Pagamento PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Pagamento_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT UQ_Pagamento_ChaveIdempotencia UNIQUE (ChaveIdempotencia),
    CONSTRAINT CK_Pagamento_Metodo CHECK (Metodo IN (1, 2, 3)),
    CONSTRAINT CK_Pagamento_Status CHECK (Status BETWEEN 1 AND 7),
    CONSTRAINT CK_Pagamento_Valor  CHECK (Valor > 0),
    CONSTRAINT CK_Pagamento_Parcelas CHECK (Parcelas IS NULL OR Parcelas BETWEEN 1 AND 12)   -- RN06
);
CREATE UNIQUE NONCLUSTERED INDEX UQ_Pagamento_GatewayId ON dbo.Pagamento (GatewayId) WHERE GatewayId IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_Pagamento_PedidoId ON dbo.Pagamento (PedidoId);
-- Job de conciliação: pagamentos pendentes/em análise
CREATE NONCLUSTERED INDEX IX_Pagamento_Status_CriadoEm ON dbo.Pagamento (Status, CriadoEm) INCLUDE (GatewayId, PedidoId);

CREATE TABLE dbo.NotificacaoGateway (
    Id                BIGINT IDENTITY(1,1) NOT NULL,
    RequestId         VARCHAR(100)         NULL,     -- x-request-id (deduplicação)
    Topico            VARCHAR(50)          NOT NULL,
    RecursoId         VARCHAR(50)          NOT NULL,
    Corpo             NVARCHAR(MAX)        NULL,
    AssinaturaValida  BIT                  NOT NULL,
    Processada        BIT                  NOT NULL CONSTRAINT DF_NotificacaoGateway_Processada DEFAULT (0),
    Erro              NVARCHAR(1000)       NULL,
    RecebidaEm        DATETIME2(3)         NOT NULL CONSTRAINT DF_NotificacaoGateway_RecebidaEm DEFAULT (SYSUTCDATETIME()),
    ProcessadaEm      DATETIME2(3)         NULL,
    CONSTRAINT PK_NotificacaoGateway PRIMARY KEY CLUSTERED (Id)
);
CREATE UNIQUE NONCLUSTERED INDEX UQ_NotificacaoGateway_RequestId ON dbo.NotificacaoGateway (RequestId) WHERE RequestId IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_NotificacaoGateway_RecebidaEm ON dbo.NotificacaoGateway (RecebidaEm);

/* ---------------------------------------------------------------------
   Frete e configurações
   --------------------------------------------------------------------- */
CREATE TABLE dbo.FaixaFrete (
    Id          INT IDENTITY(1,1) NOT NULL,
    Descricao   NVARCHAR(100)     NOT NULL,
    CepInicio   CHAR(8)           NOT NULL,
    CepFim      CHAR(8)           NOT NULL,
    Valor       DECIMAL(12,2)     NOT NULL,
    PrazoDias   INT               NOT NULL,
    Ativa       BIT               NOT NULL CONSTRAINT DF_FaixaFrete_Ativa DEFAULT (1),
    CriadoEm    DATETIME2(3)      NOT NULL CONSTRAINT DF_FaixaFrete_CriadoEm DEFAULT (SYSUTCDATETIME()),
    AlteradoEm  DATETIME2(3)      NULL,
    CONSTRAINT PK_FaixaFrete PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_FaixaFrete_Faixa CHECK (CepInicio <= CepFim AND LEN(CepInicio) = 8 AND LEN(CepFim) = 8),
    CONSTRAINT CK_FaixaFrete_Valores CHECK (Valor >= 0 AND PrazoDias > 0)
);
CREATE NONCLUSTERED INDEX IX_FaixaFrete_Cep ON dbo.FaixaFrete (CepInicio, CepFim) INCLUDE (Valor, PrazoDias, Ativa);

CREATE TABLE dbo.Configuracao (
    Chave        VARCHAR(100)   NOT NULL,
    Valor        NVARCHAR(500)  NOT NULL,
    AlteradoEm   DATETIME2(3)   NULL,
    AlteradoPor  NVARCHAR(100)  NULL,
    CONSTRAINT PK_Configuracao PRIMARY KEY CLUSTERED (Chave)
);

INSERT INTO dbo.Configuracao (Chave, Valor) VALUES
    ('Frete.CepOrigem',        N'13270000'),  -- ajustar para o CEP de onde os pedidos são postados
    ('Frete.DiasManuseio',     N'1'),
    ('Frete.GratisAPartirDe',  N'0'),         -- 0 = sem frete grátis
    ('Pagamento.ParcelasMaximas', N'12'),
    ('Pagamento.ParcelaMinima',   N'10.00');
