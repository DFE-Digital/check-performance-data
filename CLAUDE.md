# CLAUDE.md

Guidance for Claude Code in this repository is in [AGENTS.md](AGENTS.md). All coding agents on
this project share that file.

@AGENTS.md

## Claude Code specifics

This section describes Claude Code's own configuration only. Put project rules in
[AGENTS.md](AGENTS.md), not here.

- **`.claude/settings.json`** is committed and shared. It lets Claude build, run tests, and run
  read-only `git` and `gh` commands without a prompt. It stops Claude reading `.env` files and
  key or certificate files.
- **`.claude/skills/playwright-cli/`** is the Playwright CLI skill. It drives a browser to check
  pages by hand.
- **`.claude/settings.local.json`** and **`CLAUDE.local.md`** are git-ignored and personal. Put
  your own permissions and preferences there. Do not commit them.

If you change `.claude/settings.json` or `.claude/skills/`, update this section in the same pull
request.
