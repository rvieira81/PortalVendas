# ADR-004: Autenticação por cookie com tabela própria e TOTP para administradores

**Contexto:** precisamos de login de cliente, bloqueio por tentativas, confirmação de e-mail, redefinição de senha e MFA para administradores, com esquema de banco controlado por scripts (ADR-003).

**Opções consideradas:** (a) ASP.NET Core Identity completo; (b) Cookie Authentication + `PasswordHasher<T>` + tabela `Usuario` própria + TOTP (RFC 6238); (c) provedor externo (Entra ID, Auth0).

**Decisão:** (b). O hash de senha usa o `PasswordHasher` do ASP.NET Core (PBKDF2-HMAC-SHA512, 100 mil iterações, com rehash automático quando o padrão evolui). O TOTP segue a RFC 6238 (SHA-1, 6 dígitos, 30 s, tolerância de ±1 janela) e é testado com os vetores oficiais.

**Consequências:** esquema enxuto e legível para o DBA, sem as sete tabelas do Identity. Em compensação, o fluxo de autenticação é código nosso e precisa de testes — cobertos em `ContaServiceTests` e `TotpTests`. Login social (Google, Apple) fica para uma versão futura.
