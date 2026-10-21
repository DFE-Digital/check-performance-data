# Feature: Isolated agent environment

## Steps
1. Restore .agents/Containerfile, build-context exclusions and entrypoints/dashboard from verified non-secret image files (R2, R5, R8–R9). Check: ./agent build succeeds; Codex runs and npm prefix/cache point into dedicated state.
2. Restore .agents/compose.yaml and executable agent wrapper (R1–R4). Check: resolved configuration and actual mounts contain only repository bind and dedicated volumes, proxy becomes healthy and checkout DNS is unique.
3. Restore .agents/tests/isolation.sh and mounts.py (R7–R9). Check: ./agent test-isolation returns zero with all PASS; browser serves numeric stats and rejects sensitive routes; proxy rejects missing/incorrect tokens.
4. Restore .agents/skills/isolation-check/SKILL.md and README.md (R6, R10). Check: documented paths/commands agree with the implementation and existing skills remain untouched.
5. Review scoped staged changes, run secret scanning and commit on GG/containedAgent (R10). Check: scanner passes, commit exists and working tree is clean.

## Coverage
| Requirement | Steps |
|---|---|
| R1, R3, R4 | 2 |
| R2, R5 | 1, 2 |
| R6 | 4 |
| R7 | 3 |
| R8, R9 | 1, 3 |
| R10 | 4, 5 |

## Definition of done
Images build; isolation, DNS/token, dashboard and writable npm checks pass. No application source or credentials are committed. Restored setup is committed on the requested branch; any verification failures are reported.
