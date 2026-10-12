#!/usr/bin/env bash

set -eu

### Check Microsoft.Playwright NuGet pin matches playwright/dotnet image tag ###
#
# The C# Playwright bindings and the bundled Chromium binary in the docker
# image are released as a matched pair. If the NuGet package version drifts
# from the image tag, tests fail at runtime with a cryptic protocol-mismatch
# error ("Browser was not installed at this version").
#
# The canonical image tag lives in tests/.../E2ETests/Dockerfile (FROM line)
# because docker-compose.yaml uses build:, not image:, for the e2e-tests
# service. A pointer comment in docker-compose.yaml next to that build:
# block tells readers where the tag is pinned; this script enforces it.
#
# Run this from the repo root:
#   bash scripts/check-playwright-pin.sh
# Exit 0: pins match.
# Exit 1: pins drift; message names both versions and the files to fix.

CSPROJ_FILE="tests/DfE.CheckPerformanceData.E2ETests/DfE.CheckPerformanceData.E2ETests.csproj"
DOCKERFILE="tests/DfE.CheckPerformanceData.E2ETests/Dockerfile"

if [ ! -f "$CSPROJ_FILE" ]; then
    echo "ERROR: $CSPROJ_FILE not found. Run this script from the repo root."
    exit 1
fi
if [ ! -f "$DOCKERFILE" ]; then
    echo "ERROR: $DOCKERFILE not found. Run this script from the repo root."
    exit 1
fi

NUGET_VER=$(grep -oE 'Microsoft\.Playwright" Version="[0-9.]+' "$CSPROJ_FILE" \
    | head -n1 \
    | sed -E 's/.*Version="//')
IMAGE_VER=$(grep -oE 'mcr\.microsoft\.com/playwright/dotnet:v[0-9.]+' "$DOCKERFILE" \
    | head -n1 \
    | sed -E 's|.*:v||')

if [ -z "$NUGET_VER" ]; then
    echo "ERROR: could not extract Microsoft.Playwright version from $CSPROJ_FILE."
    exit 1
fi
if [ -z "$IMAGE_VER" ]; then
    echo "ERROR: could not extract image tag from $DOCKERFILE."
    exit 1
fi

if [ "$NUGET_VER" != "$IMAGE_VER" ]; then
    echo "ERROR: Microsoft.Playwright NuGet version ($NUGET_VER) does not match"
    echo "       playwright/dotnet image tag ($IMAGE_VER)."
    echo
    echo "These must move in lockstep — Chromium binary protocol mismatch otherwise."
    echo "Update one of:"
    echo "  $CSPROJ_FILE        (Microsoft.Playwright + Microsoft.Playwright.Xunit)"
    echo "  $DOCKERFILE        (FROM mcr.microsoft.com/playwright/dotnet:v...)"
    exit 1
fi

echo "Playwright pin check OK: NuGet $NUGET_VER == image v$IMAGE_VER"
