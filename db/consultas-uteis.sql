/* =====================================================================
   PortalVendas — consultas úteis para o SSMS (somente leitura)
   Visual Studio / dotnet run:  servidor (localdb)\MSSQLLocalDB
   IIS local (deploy\iis-local.ps1): servidor localhost (ou a instância usada)
   Banco: PortalVendas · Autenticação do Windows
   ===================================================================== */
USE PortalVendas;

-- Scripts de banco já aplicados (V001, V002, seeds)
SELECT Versao, Script, AplicadoEm FROM dbo.SchemaVersao ORDER BY Versao;

-- Últimos pedidos com cliente, canal (1 Site, 2 WhatsApp) e situação
SELECT TOP (50) p.Id, p.CriadoEm, u.Nome AS Cliente, u.Email,
       CASE p.Origem WHEN 2 THEN 'WhatsApp' ELSE 'Site' END AS Canal,
       p.Status, p.Total, p.FreteDescricao, p.CodigoRastreio
  FROM dbo.Pedido p JOIN dbo.Usuario u ON u.Id = p.UsuarioId
 ORDER BY p.Id DESC;

-- Pagamentos do último pedido
SELECT pg.* FROM dbo.Pagamento pg WHERE pg.PedidoId = (SELECT MAX(Id) FROM dbo.Pedido);

-- Produtos e estoque
SELECT p.Id, p.Sku, p.Nome, c.Nome AS Categoria, p.Preco, p.PrecoDe, p.Estoque, p.Ativo
  FROM dbo.Produto p JOIN dbo.Categoria c ON c.Id = p.CategoriaId
 ORDER BY c.Nome, p.Nome;

-- Usuários (administradores e clientes; contas criadas no WhatsApp têm SenhaHash = '!whatsapp')
SELECT Id, Nome, Email, Perfil, EmailConfirmado, TotpAtivo,
       CASE WHEN SenhaHash = '!whatsapp' THEN 'WhatsApp' ELSE 'Site' END AS Origem, CriadoEm
  FROM dbo.Usuario ORDER BY Id DESC;

-- Conversas do WhatsApp e últimas mensagens
SELECT TOP (20) c.Id, c.Telefone, c.NomeContato, c.UsuarioId, c.Etapa, c.AtendimentoHumano, c.NaoLidas, c.UltimaMensagemEm
  FROM dbo.ConversaWhatsApp c ORDER BY c.UltimaMensagemEm DESC;
SELECT TOP (50) m.ConversaId, CASE m.Entrada WHEN 1 THEN 'cliente' ELSE ISNULL(m.Autor, 'loja') END AS De, m.Texto, m.Erro, m.CriadoEm
  FROM dbo.MensagemWhatsApp m ORDER BY m.Id DESC;

-- Configurações (tokens do WhatsApp aparecem cifrados)
SELECT Chave, CASE WHEN Chave LIKE '%Token%' OR Chave LIKE '%Secret%' OR Chave LIKE '%ApiKey%' OR Chave LIKE '%ChaveApi%'
                   THEN '(cifrado)' ELSE Valor END AS Valor, AlteradoEm, AlteradoPor
  FROM dbo.Configuracao ORDER BY Chave;

-- Notificações recebidas do Mercado Pago
SELECT TOP (20) Id, Topico, RecursoId, AssinaturaValida, Processada, Erro, RecebidaEm FROM dbo.NotificacaoGateway ORDER BY Id DESC;
