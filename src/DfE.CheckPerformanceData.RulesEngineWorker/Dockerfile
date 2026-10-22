# Each base image is pinned by digest, which Dependabot keeps current (docs/dependabot.md#pins).
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS base
# `apt-get upgrade` applies Ubuntu security updates the base image has not yet
# been rebuilt with (see the Web Dockerfile for the CVE-2026-84782 example).
# curl is used by the container orchestrator's exec probe to query the HTTP health surface.
RUN apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
ENV NUGET_CERT_REVOCATION_MODE=offline
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Restore once, in locked mode, from the committed packages.lock.json files (#613); the worker
# project and everything it references, so the cached layer holds the whole graph.
COPY Directory.Packages.props Directory.Build.props ./
COPY DfE.CheckPerformanceData.Domain/DfE.CheckPerformanceData.Domain.csproj DfE.CheckPerformanceData.Domain/packages.lock.json DfE.CheckPerformanceData.Domain/
COPY DfE.CheckPerformanceData.Application/DfE.CheckPerformanceData.Application.csproj DfE.CheckPerformanceData.Application/packages.lock.json DfE.CheckPerformanceData.Application/
COPY DfE.CheckPerformanceData.Persistence/DfE.CheckPerformanceData.Persistence.csproj DfE.CheckPerformanceData.Persistence/packages.lock.json DfE.CheckPerformanceData.Persistence/
COPY DfE.CheckPerformanceData.Infrastructure/DfE.CheckPerformanceData.Infrastructure.csproj DfE.CheckPerformanceData.Infrastructure/packages.lock.json DfE.CheckPerformanceData.Infrastructure/
COPY DfE.CheckPerformanceData.RulesEngineWorker/DfE.CheckPerformanceData.RulesEngineWorker.csproj DfE.CheckPerformanceData.RulesEngineWorker/packages.lock.json DfE.CheckPerformanceData.RulesEngineWorker/
RUN dotnet restore "DfE.CheckPerformanceData.RulesEngineWorker/DfE.CheckPerformanceData.RulesEngineWorker.csproj" --locked-mode
COPY . .
WORKDIR "/src/DfE.CheckPerformanceData.RulesEngineWorker"
# --no-restore on build and publish: the locked restore above is the only restore this image does.
RUN dotnet build "./DfE.CheckPerformanceData.RulesEngineWorker.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DfE.CheckPerformanceData.RulesEngineWorker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DfE.CheckPerformanceData.RulesEngineWorker.dll"]
