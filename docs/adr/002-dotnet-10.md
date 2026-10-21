# ADR-002: .NET 10 LTS

**Contexto:** projeto novo em outubro de 2026. O .NET 8 (LTS) encerra o suporte em 10/11/2026; o .NET 10 (LTS) foi lançado em novembro de 2025, com suporte até novembro de 2028.

**Opções consideradas:** .NET 8, .NET 9 (STS, suporte até nov/2026), .NET 10.

**Decisão:** .NET 10. A versão fica em `Directory.Build.props` e as versões dos pacotes em `Directory.Packages.props`.

**Consequências:** o servidor precisa do ASP.NET Core Hosting Bundle 10 e a máquina de desenvolvimento do SDK 10 (Visual Studio 2026, ou VS 2022 com o SDK instalado à parte). Se for obrigatório usar .NET 8, basta trocar `net10.0` por `net8.0` e os pacotes `10.0.x` por `8.0.x` nesses dois arquivos — o código não usa recursos exclusivos do .NET 10.
