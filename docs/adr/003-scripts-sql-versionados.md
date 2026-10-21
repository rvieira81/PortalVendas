# ADR-003: Esquema do banco por scripts SQL versionados

**Contexto:** o banco roda em SQL Server próprio, administrado pela mesma equipe que mantém outros sistemas em SQL Server. Precisamos de collation específica para busca sem acento, CHECK constraints, índices filtrados e revisão de cada mudança.

**Opções consideradas:** (a) EF Core Migrations geradas pelo código; (b) scripts SQL numerados aplicados por um script PowerShell; (c) projeto de banco do SSDT (.sqlproj/dacpac).

**Decisão:** (b). Scripts `db/migrations/VNNN__descricao.sql`, idempotentes, aplicados em ordem por `db/deploy-db.ps1`, que registra cada versão em `dbo.SchemaVersao`. O EF Core só mapeia as tabelas; nunca cria nem altera o esquema em produção.

**Consequências:** o DBA revê o DDL exato antes da implantação, e o mesmo script roda em desenvolvimento, homologação e produção. Toda mudança de entidade exige um novo script — o PR deve trazer os dois. Nos testes automatizados, o EF cria o esquema no SQLite em memória a partir do modelo (`EnsureCreated`), por isso o mapeamento EF não usa nada específico de SQL Server.
