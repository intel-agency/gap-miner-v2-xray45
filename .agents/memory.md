# Project Memory

## Current Activity

### 2026-11-12 — rev2 pipeline run: init-success → implementation:ready on story #6

- Trigger: delivery cbadf160 (label gh-issue-tracking:init-success on plan #2). Verified live state via gh: hierarchy intact (1 plan + 7 epics #4/#11/#17/#24/#32/#34/#39 + 22 stories, all open).
- Advanced ONE unit: applied `implementation:ready` to #6 (Story 1.1: Repository Bootstrap (T-0.1)) — lowest-numbered OPEN story, no linked PRs. Status + outcome comments posted on #2. Next run implements story #6.
- Fixed markdownlint failures (MD022/MD012/MD047) left in this file by the previous run's uncommitted entry; committed both entries to `development` (.agents bookkeeping, no-PR run).

### 2026-11-12 — gh-issue-tracking-init on gap-miner-v2-xray45 (hierarchy live; 2 deferred items)

- Plan #2 (+ init-success label) -> 7 epics (#4/#11/#17/#24/#32/#34/#39) -> 22 stories; 34 blocked-by edges verified. Project #95 (gap-miner-v2-xray45) with fields/views. Milestones: Gate 1: Foundation, Gate 2: Pipelines, Gate 3: Product and Hardening.
- COMPLETE (all deferred items finished after the rate-limit cooldown lifted): all 30 issues live with bodies/labels/milestones/links/34 deps; project #95 fields+views; init-success on plan #2; all 30 items on the board with full Level/Priority/Status fields (verified: 1 plan + 7 epics + 22 stories, ALL FIELDS OK); duplicate epic #42 deleted. Repo issue count back to 31 (30 hierarchy + 1 dispatch marker).
- Incident lessons: (1) duplicated tool invocations after 'Tool result missing' created duplicate issues/projects/milestones — GitHub REST list replication lags ~1 min, so immediate re-runs of idempotent creates DUPLICATE; verify live state via REST before re-running any mutating step. (2) Rapid-fire bursts triggered a >40-min user-level GraphQL secondary limit while REST kept working — create/link/deps/labels have REST fallbacks; Projects v2 (fields) does not.

## Completed Work Items

*Finished work will be logged here.*

## Decisions

*Design decisions and their rationale will be recorded here.*

## Remember To Do

*Deferred tasks and future changes will be tracked here.*
