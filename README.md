# gap-miner-v2-xray45

This repository is a **project instance** stamped from the `nam20485/swarm-context` parent
template (GitHub template route) by the launcher in `nam20485/swarm-orchestration-service`:
it carries the agent-context base plus the swarm surfaces — `.zcode/agents` worker
definitions, `.agents/rules/swarm.md` + `swarm-workers.md`, the `$swarm` / `swarm-plan`
skills — and houses this app's plan (`plan_docs/`) and its development.

## Prerequisites

- **PowerShell 7+** (`pwsh`) — all scripts in this repo are written in cross-platform
  PowerShell.
- **GitHub CLI** (`gh`) — authenticated with `repo`, `project`, and `user:email` scopes.
  Verify with:

  ```sh
  gh auth status
  ```

- **Node.js** (optional) — only needed to run `markdownlint-cli2` for Markdown linting.

## Getting started

1. Create a new repository from this template (or clone it).
2. Read [`AGENTS.md`](AGENTS.md) — the operating manual for AI agents working in this repo.
3. Consult [`.agents/memory.md`](.agents/memory.md) for project history and current state.

## Configuration

Credentials are never committed to the repository — the root [`opencode.json`](opencode.json)
carries no `apiKey` values or `{env:VAR}` credential references; auth is resolved from the
environment / `auth.json` at runtime.

## Repository layout

| Path | Description |
|---|---|
| `AGENTS.md` | Operating manual for AI agents (coding guidelines, validation, source control) |
| `.agents/memory.md` | Durable project context: current activity, completed work, decisions |
| `.agents/rules/` | Coding conventions, tool usage, validation, and practices (one file per subject) |
| `.agents/skills/` | Agent Skills (`swarm` goal-loop swarm, `swarm-plan` interactive planning frontend, `gh-issue-tracking-init`, `update-powershell-standard`) |
| `.zcode/agents/` | ZCode agent definitions — the `swarm-*` worker set |
| `docs/` | Reference documentation and plans |
| `scripts/` | GitHub CLI helpers: auth, label sync, PR review-thread management, index refresh |
| `local_ai_instruction_modules/` | Workflow assignment and dynamic workflow lookup tables |
| `.opencode/` | OpenCode agent definitions and runtime configuration |

## Running the app (Aspire AppHost)

Prerequisites: .NET 8 SDK, the `aspire` workload, and a Docker daemon:

```sh
dotnet workload install aspire   # once per machine: DCP/dashboard binaries + Projects generator
dotnet run --project src/GapMiner.AppHost
```

This launches the Aspire dashboard on <https://localhost:18888> (login link printed to
the console) with PostgreSQL 16 (`pgvector/pgvector:pg16`, persistent data volume,
`gapminer` database connection string) and Redis 7.4 as container resources, plus the
four service projects (Api, Web, ScraperWorker, AIWorker — placeholders until their
epics build them out). Connection strings flow to the services via `IConfiguration`
(`ConnectionStrings:gapminer`, `ConnectionStrings:redis`).

> **Note (OpenSSL ≥ 3.5 distros, e.g. Debian 13):** the DCP binary shipped with
> Aspire 8.2.2 panics during its TLS 1.3 handshake against newer OpenSSL patches
> (see [golang-fips/go#344](https://github.com/golang-fips/go/issues/344),
> [microsoft/aspire#17421](https://github.com/microsoft/aspire/issues/17421)) and the
> AppHost fails with `SSL connection could not be established` / SSL EOF. Workaround:
> preload an era-matched libcrypto/libssl (OpenSSL 3.0.14 works), e.g.
> `LD_PRELOAD=/path/to/libcrypto.so.3:/path/to/libssl.so.3 dotnet run --project src/GapMiner.AppHost`.
> Fixed upstream in newer Aspire versions (≥ 13.3.1).

## Running the tests

Two suites run in CI via [`validation.ps1`](validation.ps1) (build → scan → test):

```pwsh
./validation.ps1            # everything: markdownlint, build, gitleaks, Pester, dotnet test
./validation.ps1 -Step test # Pester (skill scripts) + dotnet test (solution, coverage-gated)
```

- **Pester** covers the repo's skill-script suites (`gh-issue-tracking-init`,
  `update-powershell-standard`, `swarm`).
- **xUnit** covers the .NET solution (`src/GapMiner.Domain` entities, value objects, enums);
  line coverage is enforced above 85% by coverlet.

## Linting

Markdown linting uses `markdownlint-cli2` with configuration in [`.markdownlint.json`](.markdownlint.json).
Scope linting to changed files to avoid known pre-existing violations in
`docs/plans/new templates/` and `local_ai_instruction_modules/`:

```sh
markdownlint-cli2 README.md .agents/memory.md
```

## Contributing

- Create a new branch for each change using the form `<prefix>/<name>` (e.g. `dev/new-feature`).
- Run `/safe-commit` before committing to scan for uncommitted secrets.
- Pull requests must have a milestone and project set. See
  [`.agents/rules/source-control.md`](.agents/rules/source-control.md) for full details.

## Where to look next

- [`AGENTS.md`](AGENTS.md) — start here for agent operating instructions.
- [`.agents/memory.md`](.agents/memory.md) — project history and decisions.
- [`.agents/rules/`](.agents/rules/) — detailed conventions for each subject area.
