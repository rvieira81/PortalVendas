# ADR-007: Job em segundo plano dentro do processo do site

**Contexto:** pedidos Pix e boleto expiram, e webhooks podem se perder; é preciso um processo periódico de expiração e conciliação.

**Opções consideradas:** (a) `BackgroundService` dentro do site; (b) Windows Service separado; (c) SQL Server Agent; (d) Hangfire.

**Decisão:** (a), com o Application Pool configurado com `startMode=AlwaysRunning`, `idleTimeout=0` e `preloadEnabled=true`, para o IIS não desligar o processo.

**Consequências:** um só artefato para publicar. Se o processo cair, o job volta junto com o site, e as operações são idempotentes, então rodar atrasado não causa dano. Para mais de um servidor (web farm), seria necessário trocar para (b) ou (d), ou garantir que só uma instância rode o job.
