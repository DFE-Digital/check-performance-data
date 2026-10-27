# Each base image is pinned by digest, which Dependabot keeps current (docs/dependabot.md#pins).

# Test-runner image: Linux Chromium and the .NET 10 SDK both come from
# playwright/dotnet, which is built on mcr.microsoft.com/dotnet/sdk:10.0
# (v1.63.0 carries SDK 10.0.401). The digest below fixes the SDK version, and
# it changes only when this line does (#663).
#
# Base image tag MUST track Microsoft.Playwright NuGet version in
# tests/DfE.CheckPerformanceData.E2ETests/DfE.CheckPerformanceData.E2ETests.csproj
# (the tests/ projects are not centrally managed). Chromium binary protocol mismatch otherwise.
FROM mcr.microsoft.com/playwright/dotnet:v1.63.0-noble@sha256:aa3eaf29a65df8c93fc32ab20cf9359f4d8ecc53e6e89f79378647ace87b46b3

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
