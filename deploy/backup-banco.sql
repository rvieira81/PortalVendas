/* =====================================================================
   Backups do PortalVendas (SQL Server Agent ou Agendador de Tarefas + sqlcmd)
   Ajuste @pasta. Teste o RESTORE mensalmente (seção "Restore de teste").
   ===================================================================== */

-- 0) Modelo de recuperação FULL (permite backup de log a cada 15 min)
ALTER DATABASE PortalVendas SET RECOVERY FULL;
GO

-- 1) Completo — diário às 02:00
DECLARE @pasta NVARCHAR(200) = N'E:\Backups\SQL\PortalVendas\';
DECLARE @arquivo NVARCHAR(400) = @pasta + N'PortalVendas_FULL_' + FORMAT(SYSDATETIME(), 'yyyyMMdd_HHmm') + N'.bak';
BACKUP DATABASE PortalVendas TO DISK = @arquivo WITH COMPRESSION, CHECKSUM, INIT, STATS = 10;
RESTORE VERIFYONLY FROM DISK = @arquivo WITH CHECKSUM;
GO

-- 2) Diferencial — a cada 6 horas
DECLARE @pasta NVARCHAR(200) = N'E:\Backups\SQL\PortalVendas\';
DECLARE @arquivo NVARCHAR(400) = @pasta + N'PortalVendas_DIFF_' + FORMAT(SYSDATETIME(), 'yyyyMMdd_HHmm') + N'.bak';
BACKUP DATABASE PortalVendas TO DISK = @arquivo WITH DIFFERENTIAL, COMPRESSION, CHECKSUM, INIT;
GO

-- 3) Log — a cada 15 minutos
DECLARE @pasta NVARCHAR(200) = N'E:\Backups\SQL\PortalVendas\';
DECLARE @arquivo NVARCHAR(400) = @pasta + N'PortalVendas_LOG_' + FORMAT(SYSDATETIME(), 'yyyyMMdd_HHmm') + N'.trn';
BACKUP LOG PortalVendas TO DISK = @arquivo WITH COMPRESSION, CHECKSUM, INIT;
GO

/* 4) Restore de teste (mensal) — em um banco separado, nunca sobre produção
RESTORE FILELISTONLY FROM DISK = N'E:\Backups\SQL\PortalVendas\PortalVendas_FULL_20261020_0200.bak';

RESTORE DATABASE PortalVendas_RestoreTeste
  FROM DISK = N'E:\Backups\SQL\PortalVendas\PortalVendas_FULL_20261020_0200.bak'
  WITH MOVE N'PortalVendas' TO N'E:\SQLData\PortalVendas_RestoreTeste.mdf',
       MOVE N'PortalVendas_log' TO N'E:\SQLData\PortalVendas_RestoreTeste_log.ldf',
       NORECOVERY, REPLACE;
-- aplicar o último diferencial e os logs em ordem, com NORECOVERY, e o último com RECOVERY
RESTORE DATABASE PortalVendas_RestoreTeste WITH RECOVERY;

SELECT COUNT(*) AS Pedidos, MAX(CriadoEm) AS UltimoPedido FROM PortalVendas_RestoreTeste.dbo.Pedido;
DROP DATABASE PortalVendas_RestoreTeste;
*/
