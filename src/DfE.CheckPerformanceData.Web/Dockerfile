# Each base image is pinned by digest, which Dependabot keeps current (docs/dependabot.md#pins).

# Base runtime stage — targeted by Visual Studio Container Tools for fast-mode
# debugging. Keep the FROM image and EXPOSE in sync with the compose `web`
# service (HTTP on 8080).
#
# Stages: CI and compose build `runtime` (see the `target:` in
# .github/workflows/build-and-deploy.yml and docker-compose.yaml); only the
# docker-compose.vscode.yml override builds `debug-runtime`. Keep it that way:
# the debug stage ships a debugger and download tooling that must not reach a
# deployed environment.
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS base
# Apply Ubuntu security updates on top of the base image. Microsoft rebuilds
# the aspnet image on its own cadence, so between rebuilds it can ship packages
# with published fixes outstanding (e.g. libssl3t64 3.0.13-0ubuntu3.15 against
# CVE-2026-84782), and the Snyk gate in CI then fails every build. Upgrading at
# build time keeps the shipped image at the current noble-security level.
RUN apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080
# Run as the image's unprivileged `app` user (least privilege, #609), as the worker image does.
# $APP_UID is the numeric uid 1654 the aspnet image defines; it must stay numeric because
# Kubernetes' runAsNonRoot check reads the image's USER and refuses a name. The app writes
# nothing to disk at run time: data-protection keys persist to blob storage, and the multipart
# buffering ASP.NET does for large uploads goes to /tmp, which is world-writable.
USER $APP_UID

# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build
ENV NUGET_CERT_REVOCATION_MODE=offline
WORKDIR /src

# Use cached CRLs only for NuGet package-signature revocation checks. Online
# revocation lookups intermittently fail inside the build sandbox, surfacing as
# NU3012 "certificate revoked" chain-building errors during restore.
ENV NUGET_CERT_REVOCATION_MODE=offline

# Restore once, in locked mode, from the committed packages.lock.json files (#613). The project
# files and lock files of the web project and everything it references come first so this layer
# is cached until a package changes; the restore needs the whole graph, not a flattened copy.
COPY Directory.Packages.props Directory.Build.props ./
COPY DfE.CheckPerformanceData.Domain/DfE.CheckPerformanceData.Domain.csproj DfE.CheckPerformanceData.Domain/packages.lock.json DfE.CheckPerformanceData.Domain/
COPY DfE.CheckPerformanceData.Application/DfE.CheckPerformanceData.Application.csproj DfE.CheckPerformanceData.Application/packages.lock.json DfE.CheckPerformanceData.Application/
COPY DfE.CheckPerformanceData.Persistence/DfE.CheckPerformanceData.Persistence.csproj DfE.CheckPerformanceData.Persistence/packages.lock.json DfE.CheckPerformanceData.Persistence/
COPY DfE.CheckPerformanceData.Infrastructure/DfE.CheckPerformanceData.Infrastructure.csproj DfE.CheckPerformanceData.Infrastructure/packages.lock.json DfE.CheckPerformanceData.Infrastructure/
COPY DfE.CheckPerformanceData.Web/DfE.CheckPerformanceData.Web.csproj DfE.CheckPerformanceData.Web/packages.lock.json DfE.CheckPerformanceData.Web/
RUN dotnet restore DfE.CheckPerformanceData.Web/DfE.CheckPerformanceData.Web.csproj --locked-mode

COPY . ./
# --no-restore: the locked restore above is the only restore this image does.
RUN dotnet publish DfE.CheckPerformanceData.Web/DfE.CheckPerformanceData.Web.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

# Runtime (final) — derives from `base` so the image VS debugs and the image
# compose/CI builds share one runtime base. EXPOSE is inherited from `base`.
FROM base AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DfE.CheckPerformanceData.Web.dll"]

# VS Code debug stage — builds in Debug configuration and installs vsdbg so the
# VS Code Docker extension can attach a .NET debugger to the running container.
# The docker-compose.vscode.yml override targets this stage.
FROM build AS vscode-debug
ENV NUGET_CERT_REVOCATION_MODE=offline

RUN dotnet publish DfE.CheckPerformanceData.Web/DfE.CheckPerformanceData.Web.csproj \
    -c Debug -o /app/publish /p:UseAppHost=false --no-restore

# Derives from `base` so it carries the same OS security updates.
FROM base AS debug-runtime

# `base` runs as the app user; installing the debugger needs root. Handed back below.
USER root

# Install vsdbg — the .NET debugger used by VS Code Docker/C# Dev Kit
RUN apt-get update && apt-get install -y --no-install-recommends unzip wget && \
    rm -rf /var/lib/apt/lists/* && \
    printf 'Y\n' | wget -qO- https://aka.ms/getvsdbgsh | /bin/sh /dev/stdin -v latest -l /remote_debugger -r linux-x64 && \
    ln -s /remote_debugger/vsdbg /usr/local/bin/vsdbg

COPY --from=vscode-debug /app/publish .

# Back to the unprivileged user so the debug image runs the way the deployed one does.
USER $APP_UID

# Disable debug wait so the app starts immediately; the debugger attaches on demand
ENV COMPlus_DebugWait=0

ENTRYPOINT ["dotnet", "DfE.CheckPerformanceData.Web.dll"]
