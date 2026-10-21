/* =====================================================================
   PortalVendas — fotos (ilustrações) dos produtos de exemplo
   SÓ DESENVOLVIMENTO/HOMOLOGAÇÃO. Os arquivos ficam em src/PortalVendas.Web/imagens.
   Produto que já tem foto cadastrada não recebe a ilustração.
   ===================================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;

INSERT INTO dbo.ProdutoImagem (ProdutoId, Arquivo, Ordem)
SELECT p.Id, f.Arquivo, 0
  FROM (VALUES
        ('ELE-0001', '32c810fcd7604646a2b394fcd1e4f804.png'),
        ('ELE-0002', 'dcc43d2951b7434494210c520282e01f.png'),
        ('ELE-0003', 'f13b883d55c5439db09f4d066f9462de.png'),
        ('ELE-0004', 'b94f9c1559c04095840d8558e8d8df42.png'),
        ('CAS-0001', '3e92eb43ebaa45ff9b37d9c683e3c720.png'),
        ('CAS-0002', 'dcd30ec23f7c455aa37653c94b79b4c9.png'),
        ('CAS-0003', 'cb5ac57ddecc4649843e341e7c2a014e.png'),
        ('ESP-0001', '85c39cff118a40588fa4016a5c68c8e7.png'),
        ('ESP-0002', 'a7995204195d4ee3b3ebcf76f19bf986.png'),
        ('ESP-0003', 'c859e74c2e36444da250f9fb3c634261.png'),
        ('PAP-0001', '777fcd42023f433aa114060ab46cc0d2.png'),
        ('PAP-0002', 'c9961c4882bc493c80bceaae349c6c75.png')
       ) AS f (Sku, Arquivo)
  JOIN dbo.Produto p ON p.Sku = f.Sku
 WHERE NOT EXISTS (SELECT 1 FROM dbo.ProdutoImagem i WHERE i.ProdutoId = p.Id);
