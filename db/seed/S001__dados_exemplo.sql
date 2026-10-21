/* =====================================================================
   PortalVendas — dados de exemplo para DESENVOLVIMENTO e HOMOLOGAÇÃO
   NÃO executar em produção (o deploy-db.ps1 só roda seeds com -ComSeed)
   O administrador é criado pela aplicação:
       dotnet PortalVendas.Web.dll criar-admin admin@sualoja.com.br "Nome do Admin"
   ===================================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Categoria)
BEGIN
    INSERT INTO dbo.Categoria (Nome, Slug) VALUES
        (N'Eletrônicos', 'eletronicos'),
        (N'Casa e Cozinha', 'casa-e-cozinha'),
        (N'Esporte e Lazer', 'esporte-e-lazer'),
        (N'Papelaria', 'papelaria');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Produto)
BEGIN
    DECLARE @Eletronicos INT = (SELECT Id FROM dbo.Categoria WHERE Slug = 'eletronicos');
    DECLARE @Casa        INT = (SELECT Id FROM dbo.Categoria WHERE Slug = 'casa-e-cozinha');
    DECLARE @Esporte     INT = (SELECT Id FROM dbo.Categoria WHERE Slug = 'esporte-e-lazer');
    DECLARE @Papelaria   INT = (SELECT Id FROM dbo.Categoria WHERE Slug = 'papelaria');

    INSERT INTO dbo.Produto
        (CategoriaId, Sku, Nome, Slug, Descricao, Preco, PrecoDe, Estoque, PesoKg, AlturaCm, LarguraCm, ComprimentoCm, CriadoPor)
    VALUES
        (@Eletronicos, 'ELE-0001', N'Fone de Ouvido Bluetooth', 'fone-de-ouvido-bluetooth',
            N'Fone sem fio com cancelamento de ruído e 30 horas de bateria.', 199.90, 249.90, 25, 0.350, 8, 18, 20, N'seed'),
        (@Eletronicos, 'ELE-0002', N'Carregador Turbo USB-C 30W', 'carregador-turbo-usb-c-30w',
            N'Carregador rápido compatível com a maioria dos celulares.', 89.90, NULL, 60, 0.150, 5, 8, 12, N'seed'),
        (@Eletronicos, 'ELE-0003', N'Caixa de Som Portátil', 'caixa-de-som-portatil',
            N'Resistente à água, 12 horas de bateria.', 279.00, 329.00, 12, 0.800, 10, 10, 22, N'seed'),
        (@Eletronicos, 'ELE-0004', N'Mouse sem Fio Ergonômico', 'mouse-sem-fio-ergonomico',
            N'Sensor de alta precisão e pilha com duração de 12 meses.', 119.90, NULL, 0, 0.200, 6, 9, 14, N'seed'),
        (@Casa, 'CAS-0001', N'Cafeteira Elétrica 30 Xícaras', 'cafeteira-eletrica-30-xicaras',
            N'Jarra de vidro, função corta-pingos e placa aquecedora.', 159.90, NULL, 15, 2.100, 32, 22, 28, N'seed'),
        (@Casa, 'CAS-0002', N'Jogo de Panelas Antiaderente 5 Peças', 'jogo-de-panelas-antiaderente-5-pecas',
            N'Revestimento antiaderente e cabos com toque frio.', 349.90, 429.90, 8, 5.400, 25, 35, 45, N'seed'),
        (@Casa, 'CAS-0003', N'Garrafa Térmica 1 Litro', 'garrafa-termica-1-litro',
            N'Mantém a bebida quente por 12 horas.', 79.90, NULL, 40, 0.600, 30, 10, 10, N'seed'),
        (@Esporte, 'ESP-0001', N'Tapete de Yoga 6 mm', 'tapete-de-yoga-6mm',
            N'Antiderrapante, com alça para transporte.', 99.90, NULL, 30, 1.100, 12, 12, 62, N'seed'),
        (@Esporte, 'ESP-0002', N'Garrafa Squeeze 750 ml', 'garrafa-squeeze-750ml',
            N'Livre de BPA, com bico retrátil.', 39.90, NULL, 100, 0.150, 24, 8, 8, N'seed'),
        (@Esporte, 'ESP-0003', N'Kit Elásticos de Resistência', 'kit-elasticos-de-resistencia',
            N'Cinco níveis de resistência, com bolsa.', 69.90, 89.90, 3, 0.400, 5, 15, 20, N'seed'),
        (@Papelaria, 'PAP-0001', N'Caderno Universitário 10 Matérias', 'caderno-universitario-10-materias',
            N'200 folhas, capa dura.', 34.90, NULL, 80, 0.900, 3, 21, 28, N'seed'),
        (@Papelaria, 'PAP-0002', N'Caneta Gel Kit com 12 Cores', 'caneta-gel-kit-12-cores',
            N'Ponta 0,7 mm, tinta de secagem rápida.', 29.90, NULL, 120, 0.120, 2, 10, 16, N'seed');
END;

-- Tabela de contingência (usada só se a API do Melhor Envio estiver fora)
IF NOT EXISTS (SELECT 1 FROM dbo.FaixaFrete)
BEGIN
    INSERT INTO dbo.FaixaFrete (Descricao, CepInicio, CepFim, Valor, PrazoDias) VALUES
        (N'Capital e Grande São Paulo', '01000000', '09999999', 19.90, 4),
        (N'Interior de São Paulo',      '10000000', '19999999', 24.90, 5),
        (N'RJ, ES e MG',                '20000000', '39999999', 34.90, 7),
        (N'Nordeste',                   '40000000', '65999999', 49.90, 12),
        (N'Norte e Centro-Oeste',       '66000000', '79999999', 54.90, 14),
        (N'Sul',                        '80000000', '99999999', 39.90, 8);
END;
