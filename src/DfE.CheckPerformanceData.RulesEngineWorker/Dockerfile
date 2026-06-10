FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# curl is used by the container orchestrator's exec probe to query the HTTP health surface.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV NUGET_CERT_REVOCATION_MODE=offline
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Directory.Packages.props ./
COPY ["DfE.CheckPerformanceData.RulesEngineWorker/DfE.CheckPerformanceData.RulesEngineWorker.csproj", "DfE.CheckPerformanceData.RulesEngineWorker/"]
RUN dotnet restore "DfE.CheckPerformanceData.RulesEngineWorker/DfE.CheckPerformanceData.RulesEngineWorker.csproj"
COPY . .
WORKDIR "/src/DfE.CheckPerformanceData.RulesEngineWorker"
RUN dotnet build "./DfE.CheckPerformanceData.RulesEngineWorker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DfE.CheckPerformanceData.RulesEngineWorker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DfE.CheckPerformanceData.RulesEngineWorker.dll"]
