FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
