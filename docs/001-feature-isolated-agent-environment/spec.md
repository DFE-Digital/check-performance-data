# Feature: Isolated agent environment

## Requirements
1. R1: Rootless agents bind only this repository at writable /workspace, with /workspace as working directory; host home, parent, SSH/config directories and engine sockets are absent.
2. R2: Codex authentication/configuration, Headroom state and proxy token persist in dedicated named volumes.
3. R3: Services join agent-net; Codex reaches a checkout-specific Headroom DNS alias, authenticated by a token header, without host wrapping.
4. R4: Executable ./agent supports codex, shell, build, up, down, logs and test-isolation, shows startup progress and returns failures.
5. R5: A reusable development image includes the requested tools and Codex, with separate targets for future harnesses.
6. R6: Codex discovers repository skills at .agents/skills, including isolation-check.
7. R7: Host mount-source inspection and container tests check read/write, Git, parent/home/SSH/config/socket absence, unique proxy DNS and token acceptance.
8. R8: Read-only browser statistics are available on 127.0.0.1:18787, excluding credentials, prompts and arbitrary routes.
9. R9: Codex's update prompt stays enabled; npm installs/cache remain writable in dedicated state and updated binaries survive container recreation.
10. R10: Documentation explains setup, auth, skills, harnesses, application DNS/networking, updates and security boundaries. All restored files are committed to the current branch.

## Decisions
- Follows 000-project/D-1: rootless Podman Compose and mount-based isolation.
- D-7: Restore the previously verified Codex 0.159.3 image baseline and Headroom 0.39.1; user-volume Codex updates supersede the baseline.
- D-8: Reuse checkout-derived project/volume names and recover only non-secret image startup scripts, never authentication files.
- D-9: Read-only root filesystems, dropped capabilities, no-new-privileges and private writable homes/tmp.

## Open questions
None.

## Test coverage
| Check | Requirements |
|---|---|
| Build, CLI and npm-path smoke checks | R5, R9 |
| Host mount inspection and container isolation suite | R1–R3, R7 |
| Wrapper syntax, startup and exit statuses | R4 |
| Browser positive/negative route tests | R8 |
| Skills/docs, staged diff, secret scanner and commit | R6, R10 |
