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

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080


ENTRYPOINT ["dotnet", "DfE.CheckPerformanceData.Web.dll"]
