# Project Memory

## Current Activity

### 2026-11-12 — rev2 pipeline run: implementation:complete on story #6 (PR #44 review → merged)

- Trigger: delivery 5e919330 (label implementation:complete on story #6). Dispatched 3 subagent reviewers on PR #44 with disjoint scopes (AC conformance / security & CI / MSBuild semantics): two clean, one minor finding — `.editorconfig` code-style severities (file_scoped:warning) are inert in `dotnet build` without `EnforceCodeStyleInBuild`. Fixed in c9759e0 (one property in Directory.Build.props, validated: build 0 errors, gitleaks clean, Pester 266/266, coverage 95.38%), thread replied + resolved via GraphQL → 0 unresolved.
- Merge gate re-verified on head c9759e0 (all checks green, MERGEABLE/CLEAN, 0 unresolved threads) → PR #44 squash-merged as 7f82ef5; story #6 auto-closed via `Closes #6`; board #95 card → Status: Done. Pipeline advanced: `implementation:ready` applied to #8 (Story 1.2: Aspire AppHost Skeleton; its only blocker #6 is closed).
- Review-subagent contract that worked: each reviewer got ONE scope, exact file list + head SHA, a ready-to-use jq/REST command template for posting the review, read-only constraints, and a fixed report format — all three returned within one round with verifiable evidence (review IDs).
- gh gotchas hit this run: `gh api graphql -f query=@file` sends the literal string — use uppercase `-F query=@file` to read from file; `addPullRequestReviewThreadReply` takes NO `inReplyTo` argument (thread ID alone anchors the reply); this gh version's `gh project item-list --format json` omits fieldValues — verify board updates by GraphQL node query on the ProjectV2Item id; `gh project item-edit <number> --owner <org> --url <issue-url> --field Status --value Done`.
- Reconfirmed: local .NET 8 SDK needs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` before `validation.ps1` (global.json 8.0.x pin does not roll to SDK 10).

### 2026-11-12 — rev2 pipeline run: implementation:ready on story #6 (Repository Bootstrap)

- Trigger: delivery 5cd35350 (label implementation:ready on story #6). Implemented Story 1.1 (T-0.1) on branch `mn/story-1-1-repository-bootstrap`: global.json (8.0.404 + rollForward latestFeature), Directory.Packages.props (CPM, 18 pinned PackageVersion entries covering every plan §3 row that maps to NuGet), Directory.Build.props (TreatWarningsAsErrors/Nullable/ImplicitUsings), .editorconfig (file-scoped namespaces warning, 4-space, CRLF, md/sln exceptions), GapMiner.sln with src+tests solution folders; `dotnet build GapMiner.sln` exit 0.
- Gotcha: `dotnet sln add-folder` does NOT exist in .NET 8 SDK (added in the later `dotnet sln` rewrite) — solution folders were hand-authored in the .sln via the solution-folder project type GUID {2150E333-8FDC-42A3-9474-1A3956D46DE8} + empty NestedProjects section.
- Environment: this machine had only SDK 10.0.403 in /usr/share/dotnet; installed 8.0.426 to ~/.dotnet via dotnet-install.sh (export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH) because global.json pinning 8.0.x + latestFeature does not roll forward across majors.
- Validation-mirror sync: validation.ps1 Step-Build now builds GapMiner.sln; ci.yml setup-dotnet installs 8.0.x + 10.0.x (stale "no .NET product code" comment fixed). /safe-commit skill is absent from this clone — its substance (gitleaks secret scan) runs via `validation.ps1 -Step scan`, executed before every commit of this run.

### 2026-11-12 — rev2 pipeline run: init-success → implementation:ready on story #6

- Trigger: delivery cbadf160 (label gh-issue-tracking:init-success on plan #2). Verified live state via gh: hierarchy intact (1 plan + 7 epics #4/#11/#17/#24/#32/#34/#39 + 22 stories, all open).
- Advanced ONE unit: applied `implementation:ready` to #6 (Story 1.1: Repository Bootstrap (T-0.1)) — lowest-numbered OPEN story, no linked PRs. Status + outcome comments posted on #2. Next run implements story #6.
- Fixed markdownlint failures (MD022/MD012/MD047) left in this file by the previous run's uncommitted entry; committed both entries to `development` (.agents bookkeeping, no-PR run).

### 2026-11-12 — gh-issue-tracking-init on gap-miner-v2-xray45 (hierarchy live; 2 deferred items)

- Plan #2 (+ init-success label) -> 7 epics (#4/#11/#17/#24/#32/#34/#39) -> 22 stories; 34 blocked-by edges verified. Project #95 (gap-miner-v2-xray45) with fields/views. Milestones: Gate 1: Foundation, Gate 2: Pipelines, Gate 3: Product and Hardening.
- COMPLETE (all deferred items finished after the rate-limit cooldown lifted): all 30 issues live with bodies/labels/milestones/links/34 deps; project #95 fields+views; init-success on plan #2; all 30 items on the board with full Level/Priority/Status fields (verified: 1 plan + 7 epics + 22 stories, ALL FIELDS OK); duplicate epic #42 deleted. Repo issue count back to 31 (30 hierarchy + 1 dispatch marker).
- Incident lessons: (1) duplicated tool invocations after 'Tool result missing' created duplicate issues/projects/milestones — GitHub REST list replication lags ~1 min, so immediate re-runs of idempotent creates DUPLICATE; verify live state via REST before re-running any mutating step. (2) Rapid-fire bursts triggered a >40-min user-level GraphQL secondary limit while REST kept working — create/link/deps/labels have REST fallbacks; Projects v2 (fields) does not.

## Completed Work Items

- 2026-11-12 — Story 1.1: Repository Bootstrap (T-0.1) (#6): PR #44 squash-merged (7f82ef5) after 3-reviewer round + 1 finding fixed (EnforceCodeStyleInBuild). Epic 1 continues with Story 1.2 (#8).

## Decisions

*Design decisions and their rationale will be recorded here.*

## Remember To Do

*Deferred tasks and future changes will be tracked here.*
