# syntax=docker/dockerfile:1
# Build context: repository root (see infra/docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached unless project files change).
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/Trimme.BuildingBlocks.Domain/*.csproj src/BuildingBlocks/Trimme.BuildingBlocks.Domain/
COPY src/BuildingBlocks/Trimme.BuildingBlocks.Application/*.csproj src/BuildingBlocks/Trimme.BuildingBlocks.Application/
COPY src/BuildingBlocks/Trimme.BuildingBlocks.Infrastructure/*.csproj src/BuildingBlocks/Trimme.BuildingBlocks.Infrastructure/
COPY src/BuildingBlocks/Trimme.BuildingBlocks.Web/*.csproj src/BuildingBlocks/Trimme.BuildingBlocks.Web/
COPY src/Trimme.Migrations/*.csproj src/Trimme.Migrations/
COPY src/Modules/ src/Modules/
COPY apps/api/Trimme.Api/*.csproj apps/api/Trimme.Api/
RUN dotnet restore apps/api/Trimme.Api/Trimme.Api.csproj

COPY src/ src/
COPY apps/api/Trimme.Api/ apps/api/Trimme.Api/
RUN dotnet publish apps/api/Trimme.Api/Trimme.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true
COPY --from=build /app/publish .
# Non-root user provided by the official .NET images.
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
  CMD ["dotnet", "Trimme.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "Trimme.Api.dll"]
