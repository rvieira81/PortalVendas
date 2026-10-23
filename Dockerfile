# Imagem de publicação do PortalVendas.Web (ASP.NET Core, .NET 10).
# Build: docker build -t portalvendas-web .
# Execução: docker run -p 8080:8080 -e ConnectionStrings__PortalVendas="..." portalvendas-web

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia tudo para que o restore enxergue a solução, os Directory.*.props e os projetos de teste
COPY . .
RUN dotnet restore src/PortalVendas.Web/PortalVendas.Web.csproj
RUN dotnet publish src/PortalVendas.Web/PortalVendas.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Usuário sem privilégios de root dentro do contêiner
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "PortalVendas.Web.dll"]
