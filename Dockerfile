# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY SreDemo.sln ./
COPY src/SreDemo.Api/SreDemo.Api.csproj src/SreDemo.Api/
RUN dotnet restore src/SreDemo.Api/SreDemo.Api.csproj

COPY src/SreDemo.Api/ src/SreDemo.Api/
RUN dotnet publish src/SreDemo.Api/SreDemo.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM build AS test

COPY tests/SreDemo.Api.Tests/SreDemo.Api.Tests.csproj tests/SreDemo.Api.Tests/
RUN dotnet restore tests/SreDemo.Api.Tests/SreDemo.Api.Tests.csproj

COPY tests/SreDemo.Api.Tests/ tests/SreDemo.Api.Tests/
RUN dotnet test tests/SreDemo.Api.Tests/SreDemo.Api.Tests.csproj \
    --configuration Release \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble-chiseled-extra AS runtime
WORKDIR /app

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
  CMD ["dotnet", "SreDemo.Api.dll", "--health-check"]

ENTRYPOINT ["dotnet", "SreDemo.Api.dll"]
