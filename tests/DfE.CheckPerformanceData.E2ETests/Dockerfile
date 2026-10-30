# The base image is pinned by digest (docs/dependabot.md#pins). Dependabot does not update it:
# its tag follows the Microsoft.Playwright package (docs/dependabot.md#playwright).

# Test-runner image: Linux Chromium and the .NET 10 SDK both come from
# playwright/dotnet, which is built on mcr.microsoft.com/dotnet/sdk:10.0
# (v1.64.1 carries SDK 10.0.402). The digest below fixes the SDK version, and
# it changes only when this line does (#663).
#
# Base image tag MUST track Microsoft.Playwright NuGet version in
# tests/DfE.CheckPerformanceData.E2ETests/DfE.CheckPerformanceData.E2ETests.csproj
# (the tests/ projects are not centrally managed). Chromium binary protocol mismatch otherwise.
FROM mcr.microsoft.com/playwright/dotnet:v1.64.1-noble@sha256:6955861f6e17b2580c00f0970198e446f006cdae6c34fb8f9ad5318aaab50da0

ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH=/usr/share/dotnet:${PATH}
ENV DOTNET_NOLOGO=1
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV NUGET_PACKAGES=/root/.nuget/packages

# The SDK comes with the base image, so a new Playwright tag could bring a
# different .NET. Fail the build here rather than in the test run.
RUN dotnet --list-sdks | grep -E '^10\.' \
    && dotnet --list-runtimes | grep -E 'Microsoft\.NETCore\.App 10\.'

WORKDIR /work
