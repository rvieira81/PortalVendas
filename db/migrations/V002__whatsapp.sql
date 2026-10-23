/* =====================================================================
   PortalVendas — V002 — Atendimento e vendas pelo WhatsApp (módulo licenciado)
   - Pedido.Origem: de onde veio a venda (1 Site, 2 WhatsApp)
   - ConversaWhatsApp / MensagemWhatsApp: atendimento do robô e do atendente
   - TokenUsuario tipo 3: link de acesso enviado no WhatsApp para pagar no site
   - Configuracao.Valor maior: tokens das APIs ficam cifrados (Data Protection)
   ===================================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;

ALTER TABLE dbo.Pedido ADD Origem TINYINT NOT NULL CONSTRAINT DF_Pedido_Origem DEFAULT (1);
GO
ALTER TABLE dbo.Pedido ADD CONSTRAINT CK_Pedido_Origem CHECK (Origem IN (1, 2));
CREATE NONCLUSTERED INDEX IX_Pedido_Origem_CriadoEm ON dbo.Pedido (Origem, CriadoEm DESC) INCLUDE (Status, Total);

ALTER TABLE dbo.TokenUsuario DROP CONSTRAINT CK_TokenUsuario_Tipo;
ALTER TABLE dbo.TokenUsuario ADD CONSTRAINT CK_TokenUsuario_Tipo CHECK (Tipo IN (1, 2, 3));   -- 3 Acesso pelo WhatsApp

ALTER TABLE dbo.Configuracao ALTER COLUMN Valor NVARCHAR(2000) NOT NULL;

CREATE TABLE dbo.ConversaWhatsApp (
    Id                 INT IDENTITY(1,1) NOT NULL,
    Telefone           VARCHAR(20)       NOT NULL,     -- só dígitos, com DDI (5519999990000)
    NomeContato        NVARCHAR(150)     NULL,         -- nome do perfil no WhatsApp
    UsuarioId          INT               NULL,         -- cliente vinculado depois do cadastro na conversa
    Etapa              TINYINT           NOT NULL CONSTRAINT DF_ConversaWhatsApp_Etapa DEFAULT (1),
    Contexto           NVARCHAR(MAX)     NULL,         -- estado do robô (JSON)
    AtendimentoHumano  BIT               NOT NULL CONSTRAINT DF_ConversaWhatsApp_Humano DEFAULT (0),
    NaoLidas           INT               NOT NULL CONSTRAINT DF_ConversaWhatsApp_NaoLidas DEFAULT (0),
    UltimaEntradaEm    DATETIME2(3)      NULL,         -- janela de 24 h da Meta
    UltimaMensagemEm   DATETIME2(3)      NOT NULL,
    CriadoEm           DATETIME2(3)      NOT NULL CONSTRAINT DF_ConversaWhatsApp_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ConversaWhatsApp PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_ConversaWhatsApp_Telefone UNIQUE (Telefone),
    CONSTRAINT FK_ConversaWhatsApp_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id) ON DELETE SET NULL
);
CREATE NONCLUSTERED INDEX IX_ConversaWhatsApp_UltimaMensagemEm ON dbo.ConversaWhatsApp (UltimaMensagemEm DESC) INCLUDE (AtendimentoHumano, NaoLidas);

CREATE TABLE dbo.MensagemWhatsApp (
    Id          BIGINT IDENTITY(1,1) NOT NULL,
    ConversaId  INT                  NOT NULL,
    Entrada     BIT                  NOT NULL,         -- 1 cliente → loja; 0 loja → cliente
    Texto       NVARCHAR(4000)       NOT NULL,
    IdExterno   VARCHAR(200)         NULL,             -- id da mensagem no provedor (deduplicação do webhook)
    Autor       NVARCHAR(100)        NULL,             -- "robô", e-mail do atendente ou nulo (cliente)
    Processada  BIT                  NOT NULL CONSTRAINT DF_MensagemWhatsApp_Processada DEFAULT (0),
    Erro        NVARCHAR(500)        NULL,
    CriadoEm    DATETIME2(3)         NOT NULL CONSTRAINT DF_MensagemWhatsApp_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_MensagemWhatsApp PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_MensagemWhatsApp_Conversa FOREIGN KEY (ConversaId) REFERENCES dbo.ConversaWhatsApp (Id) ON DELETE CASCADE
);
CREATE UNIQUE NONCLUSTERED INDEX UQ_MensagemWhatsApp_IdExterno ON dbo.MensagemWhatsApp (IdExterno) WHERE IdExterno IS NOT NULL;
CREATE NONCLUSTERED INDEX IX_MensagemWhatsApp_Conversa ON dbo.MensagemWhatsApp (ConversaId, CriadoEm);
CREATE NONCLUSTERED INDEX IX_MensagemWhatsApp_Pendentes ON dbo.MensagemWhatsApp (CriadoEm) WHERE Entrada = 1 AND Processada = 0;
