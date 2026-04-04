# ─── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar arquivos de projeto e restaurar dependências (cache layer)
COPY ["src/Jubilados.API/Jubilados.API.csproj",            "Jubilados.API/"]
COPY ["src/Jubilados.Application/Jubilados.Application.csproj", "Jubilados.Application/"]
COPY ["src/Jubilados.Infrastructure/Jubilados.Infrastructure.csproj", "Jubilados.Infrastructure/"]
COPY ["src/Jubilados.Domain/Jubilados.Domain.csproj",      "Jubilados.Domain/"]

RUN dotnet restore "Jubilados.API/Jubilados.API.csproj"

# Copiar código-fonte e publicar
COPY src/ .
WORKDIR "/src/Jubilados.API"
RUN dotnet publish "Jubilados.API.csproj" -c Release -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ─── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Expor porta da API
EXPOSE 8080
EXPOSE 8081

ENV ASPNETCORE_URLS="http://+:8080"
ENV ASPNETCORE_ENVIRONMENT="Production"

ENTRYPOINT ["dotnet", "Jubilados.API.dll"]
