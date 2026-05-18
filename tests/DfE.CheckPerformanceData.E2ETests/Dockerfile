# Test-runner image: bundles Linux Chromium (from playwright/dotnet) +
# .NET 10 SDK (side-installed because the upstream playwright/dotnet image
# ships .NET 8 SDK only as of v1.59.0).
#
# Base image tag MUST track Microsoft.Playwright NuGet version in
# src/Directory.Packages.props. Chromium binary protocol mismatch otherwise.
FROM mcr.microsoft.com/playwright/dotnet:v1.59.0-noble

ARG DEBIAN_FRONTEND=noninteractive
ARG DOTNET_CHANNEL=10.0

# dotnet-install.sh is the officially-supported side-by-side SDK install.
# /usr/share/dotnet is where the upstream image already roots .NET; layering
# 10.x next to 8.x there means the host muxer at /usr/bin/dotnet picks up both.
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel ${DOTNET_CHANNEL} --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh

ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH=/usr/share/dotnet:${PATH}
ENV DOTNET_NOLOGO=1
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV NUGET_PACKAGES=/root/.nuget/packages

# Sanity check at build time so a missing/incompatible SDK fails fast.
RUN dotnet --list-sdks | grep -E '^10\.' \
    && dotnet --list-runtimes | grep -E 'Microsoft\.NETCore\.App 10\.'

WORKDIR /work
