# Software bill of materials (SBOM)

Every web and worker image that CI pushes to GHCR has a CycloneDX SBOM: a list of what is
inside the image, so that a new security advisory can be checked against what is deployed
without rebuilding anything (OWASP Top 10 2025, A03 software supply chain failures; #614).

## What it contains

[syft](https://github.com/anchore/syft) scans the pushed image and writes CycloneDX JSON with:

- the NuGet packages the published application carries, read from `/app/*.deps.json`
  (direct and transitive, with versions — about 90 for the web image), including the
  application itself (`DfE.CheckPerformanceData.Web`, `DfE.CheckPerformanceData.RulesEngineWorker`)
- the Ubuntu packages in the runtime image (about 100, as left by the base image plus the
  `apt-get upgrade` in the Dockerfile) and the operating system version
- the files those Ubuntu packages own

It does not contain the SDK or the build tools (they are in the build stage, not the image),
and it does not contain the test projects' packages (SixLabors.ImageSharp, Playwright, xUnit).

## Where it is

The image tag is the commit sha, and so is the SBOM's name.

1. **Workflow artifact** on the `Build and Deploy` (or `Build No Cache`) run:
   `sbom-web-<sha>.cdx.json` and `sbom-worker-<sha>.cdx.json`. Kept 90 days.
2. **Attached to the image in GHCR** as an OCI referrer, kept as long as the image. GHCR has no
   referrers API, so ORAS uses the referrers tag schema: the package gains a tag named
   `sha256-<image manifest digest>` that points at an index listing the SBOM. Those tags are the
   SBOMs; do not delete them.

The action that does this is `.github/actions/image-sbom`, called after each image build in
`.github/workflows/build-and-deploy.yml` and `.github/workflows/build-nocache.yml`.

## Finding the SBOM for a deployed environment

1. Find the sha the environment runs: the `Build and Deploy` run that deployed it (the run for
   the merge commit on `main`), or the image tag on the AKS deployment.
2. List and fetch the SBOM with [ORAS](https://oras.land) (the packages are public; no login):

   ```sh
   oras discover ghcr.io/dfe-digital/check-performance-data:<sha>
   oras pull ghcr.io/dfe-digital/check-performance-data@sha256:<digest the discover output lists>
   ```

   The pull writes `sbom-web-<sha>.cdx.json` into the current directory. The worker image is
   `ghcr.io/dfe-digital/check-performance-data-worker:<sha>`.

   Without ORAS installed, run it from its image (Linux/macOS; on Windows Git Bash prefix the
   command with `MSYS_NO_PATHCONV=1`):

   ```sh
   docker run --rm -v "$PWD:/work" -w /work ghcr.io/oras-project/oras:v1.3.5 discover ghcr.io/dfe-digital/check-performance-data:<sha>
   ```

## Checking an advisory against it

Look a package up by name:

```sh
jq '.components[] | select(.name == "Npgsql") | {name, version, purl}' sbom-web-<sha>.cdx.json
```

Or scan the whole SBOM for known vulnerabilities with [grype](https://github.com/anchore/grype):

```sh
grype sbom:./sbom-web-<sha>.cdx.json
```

The weekly `Build No Cache` run rebuilds the web image under the same sha tag with current
Ubuntu packages and attaches a new SBOM to the new image; `oras discover` by tag always shows
the SBOM of the image the tag points at now.
