# Feature: Isolated agent environment

## Problem
The previous repository-local isolated coding-agent setup was lost. Restore the final working version and commit it on the user's fresh branch.

## Who it is for
Contributors running coding-agent CLIs in this repository.

## What success looks like
Agents can read/write this repository and their dedicated state without access to the host home, parent directory, other repositories or engine sockets. Codex retains authentication and skills, reaches its own Headroom instance, updates safely when the user accepts its normal prompt, and exposes aggregate browser stats. The wrapper builds and runs the environment, isolation tests demonstrate the boundary, and the result is committed.

## Out of scope
Application source changes, additional harness installations, global host skills, production access and pushing the branch.

## Biggest assumption
Rootless containers with network egress meet the intended filesystem boundary; they are not disconnected virtual machines.
