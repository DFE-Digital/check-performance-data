#!/usr/bin/env bash

set -eu

### Check global.json names the SDK that the pinned dotnet/sdk images have (#662) ###
#
# CI tests with the SDK in global.json. The Web and worker images build with the SDK in the
# dotnet/sdk image that each Dockerfile pins by digest. Dependabot updates the two in separate
# pull requests. This check fails a pull request that moves one and not the other, so the SDK
# that runs the tests is the SDK that builds the images. See docs/dependabot.md#net-sdk.
#
# The check reads the image configuration from the registry. It does not pull the image.
#
# Run this from the repo root:
#   bash scripts/check-sdk-pin.sh
# Exit 0: each dotnet/sdk image has the SDK version that global.json names.
# Exit 1: a version differs or cannot be read. The message names the file to change.

GLOBAL_JSON="global.json"

if [ ! -f "$GLOBAL_JSON" ]; then
    echo "ERROR: $GLOBAL_JSON not found. Run this script from the repo root."
    exit 1
fi

WANTED=$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[0-9.]+"' "$GLOBAL_JSON" \
    | head -n1 \
    | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' || true)

if [ -z "$WANTED" ]; then
    echo "ERROR: could not read sdk.version from $GLOBAL_JSON."
    exit 1
fi

failed=0
unreadable=0
checked=0

for file in $(git ls-files '*Dockerfile'); do
    for image in $(grep -oE '^FROM[[:space:]]+mcr\.microsoft\.com/dotnet/sdk:[^[:space:]]+' "$file" \
        | sed -E 's/^FROM[[:space:]]+//'); do
        checked=$((checked + 1))
        versions=$(docker buildx imagetools inspect "$image" --format '{{json .Image}}' \
            | grep -oE 'DOTNET_SDK_VERSION=[0-9.]+' \
            | sed 's/.*=//' \
            | sort -u)
        if [ -z "$versions" ]; then
            echo "ERROR: $file: could not read DOTNET_SDK_VERSION from $image"
            unreadable=1
        elif [ "$versions" != "$WANTED" ]; then
            echo "ERROR: $file: the image has SDK $(echo $versions), but $GLOBAL_JSON names $WANTED."
            failed=1
        fi
    done
done

if [ "$checked" -eq 0 ]; then
    echo "ERROR: no Dockerfile has a FROM mcr.microsoft.com/dotnet/sdk line. Update this script."
    exit 1
fi

if [ "$unreadable" -ne 0 ] || [ "$failed" -ne 0 ]; then
    if [ "$unreadable" -ne 0 ]; then
        echo
        echo "Check that 'docker buildx imagetools inspect' works and that mcr.microsoft.com answers,"
        echo "then run the job again."
    fi
    if [ "$failed" -ne 0 ]; then
        echo
        echo "Make global.json and every dotnet/sdk FROM line name the same SDK in this pull request:"
        echo "change sdk.version in $GLOBAL_JSON, or the dotnet/sdk digest in each Dockerfile."
        echo "See docs/dependabot.md#net-sdk."
    fi
    exit 1
fi

echo "SDK pin check OK: global.json and $checked dotnet/sdk image(s) have SDK $WANTED"
