# Decisions log: Feature Isolated agent environment

Append-only. Changed decisions receive a new row citing the superseded decision.

| ID | Date | Stage | Question | Answer | Reasoning | Status |
|---|---|---|---|---|---|---|
| D-1 | 2026-10-21 | intent | What is being recovered? | The final working setup from this conversation, including subsequent fixes. | Explicit user request. | Agreed |
| D-2 | 2026-10-21 | intent | Which host files may agents access? | This repository and dedicated state/config volumes only. | Original security requirement. | Agreed |
| D-3 | 2026-10-21 | intent | Must authentication survive? | Yes, in a dedicated Codex volume. | Previously agreed behaviour. | Agreed |
| D-4 | 2026-10-21 | intent | Should Codex retain its update prompt? | Yes; updates install into its writable state volume. | Explicit correction earlier in the conversation. | Agreed |
| D-5 | 2026-10-21 | intent | How should stats be viewed? | A read-only numeric dashboard on host loopback port 18787. | Previously implemented and requested browser access. | Agreed |
| D-6 | 2026-10-21 | intent | What is the main assumption? | Rootless containers with network egress satisfy the intended filesystem boundary. | Providers and applications require networking. | Assumed |
| D-7 | 2026-10-21 | spec | Which image versions should be restored? | Previously verified Codex 0.159.3 bootstrap and Headroom 0.39.1, with state-volume CLI updates. | Restore the agreed setup without an unrelated dependency migration. | Assumed |
| D-8 | 2026-10-21 | spec | How should existing state be handled? | Preserve checkout-derived volume names; recover only startup scripts from surviving images. | Retain existing login without reading or committing secrets. | Assumed |
| D-9 | 2026-10-21 | spec | Which container restrictions apply? | Read-only root, unprivileged users, dropped capabilities and no-new-privileges. | Maintain the agreed filesystem boundary. | Agreed |
| D-10 | 2026-10-21 | verification | Did restoration checks pass? | Both images built; host mount inspection and all isolation tests passed with zero failures; loopback dashboard returned HTTP 200; Codex 0.160.0 and its authentication file survived in the original volume; update checks remain enabled; staged secret scan passed. | Verified on the restored branch without printing credentials or making a model request. | Agreed |
