#!/usr/bin/env bash

set -eu

### Check third-party actions are pinned to commit SHAs and base images to digests (#617) ###
#
# A tag or a branch can be moved to point at other code. A commit SHA or an
# image digest cannot. Dependabot keeps the pins current (.github/dependabot.yml).
#
# Actions follow the DfE platform team's rule (teacher-services-cloud
# documentation/github.md): pin community actions and actions from publishers
# GitHub has not verified, and leave these on a major version or branch:
#   - local actions (./...), which change with this repository
#   - official GitHub actions (actions/*, github/*)
#   - first-party DfE actions (DFE-Digital/*)
#   - verified publishers we use, listed in VERIFIED_OWNERS below
# See docs/dependabot.md#pins.
#
# Run this from the repo root:
#   bash scripts/check-pins.sh
# Exit 0: everything that must be pinned is.
# Exit 1: each unpinned reference is listed with its file and line.

# Owners shown as a verified creator on GitHub Marketplace. Check the badge on the action's
# Marketplace page before adding one.
VERIFIED_OWNERS="Azure anchore"

failed=0

for file in .github/workflows/*.yml .github/actions/*/action.yml; do
    while IFS= read -r match; do
        line=${match%%:*}
        text=${match#*:}
        ref=$(printf '%s' "$text" | sed -E 's/^[[:space:]-]*uses:[[:space:]]*//; s/[[:space:]]+#.*$//')
        owner=${ref%%/*}
        case "$ref" in
            ./* | actions/* | github/* | DFE-Digital/*) continue ;;
        esac
        if printf ' %s ' "$VERIFIED_OWNERS" | grep -qi " $owner "; then
            continue
        fi
        if ! printf '%s' "$text" | grep -qE '@[0-9a-f]{40}[[:space:]]+#[[:space:]]*v[0-9]'; then
            echo "$file:$line: $ref is not pinned to a commit SHA with its version in a comment"
            failed=1
        fi
    done < <(grep -nE '^[[:space:]-]*uses:' "$file" || true)
done

# An earlier stage (FROM base AS final) and the empty scratch image need no digest; an image
# from a registry does.
for file in $(git ls-files '*Dockerfile'); do
    awk -v file="$file" '
        toupper($1) == "FROM" {
            i = 2
            while ($i ~ /^--/) i++
            image = $i
            if (!(tolower(image) in stages) && image != "scratch" && image !~ /@sha256:[0-9a-f]{64}$/) {
                printf "%s:%d: %s is not pinned to a digest\n", file, NR, image
                bad = 1
            }
            if (tolower($(i + 1)) == "as") stages[tolower($(i + 2))] = 1
        }
        END { exit bad }
    ' "$file" || failed=1
done

if [ "$failed" -ne 0 ]; then
    echo
    echo "Pin each such action as owner/repo@<commit sha> # vX.Y.Z and each base image as"
    echo "image:tag@sha256:<digest>. docs/dependabot.md#pins says how to find them."
    exit 1
fi

echo "Pin check OK: every third-party action is pinned to a commit SHA and every base image to a digest"
