# Base runtime stage — targeted by Visual Studio Container Tools for fast-mode
# debugging. Keep the FROM image and EXPOSE in sync with the compose `web`
# service (HTTP on 8080).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV NUGET_CERT_REVOCATION_MODE=offline
WORKDIR /src

# Use cached CRLs only for NuGet package-signature revocation checks. Online
# revocation lookups intermittently fail inside the build sandbox, surfacing as
# NU3012 "certificate revoked" chain-building errors during restore.
ENV NUGET_CERT_REVOCATION_MODE=offline

COPY Directory.Packages.props ./
COPY DfE.CheckPerformanceData.Web/*.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish DfE.CheckPerformanceData.Web/DfE.CheckPerformanceData.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime (final) — derives from `base` so the image VS debugs and the image
# compose/CI builds share one runtime base. EXPOSE is inherited from `base`.
FROM base AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DfE.CheckPerformanceData.Web.dll"]
