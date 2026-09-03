# Changelog

All notable changes to the Utos CLI are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0]

### Changed
- **Adopts spec 0.0.14: an `onEmitted` rule carries an action.** A rule is a guard plus exactly
  one of `handle`, `transition` or `result`, where it was a flat dispatch. Only a `handle` names a
  document, so only a `handle` is an alias site — a `transition` names an activity in the
  consuming workflow and a `result` names nothing, and rewriting either would corrupt a value that
  was never a dependency reference. Bumped the Utos SDK packages to `0.0.14`
- **The registry-reference error no longer carries a `UTOS-S010` code**, just its message. A
  registry reference is valid per the source format — the document is correct and this tool has
  not built resolution yet, so a code said the author wrote something wrong when they had not, and
  burned a slot in a range every implementation shares. The error itself is unchanged and stays
  until the OCI work lands. `SourceIssue.Code` is now optional for exactly this case, and renders
  without it. See utos/api#35
- **Adopts spec 0.0.13: dispatched work is its own document.** A promise branch and an `onEmitted`
  rule name a document — `workflow`, `startActivity`, `input` — instead of pointing at an activity
  in the dispatching one, so resolution now rewrites aliases at three kinds of site rather than
  one. `PromiseBranch.target` is gone.
- **`self` resolves to the document it is written in**, and is legal only on a promise branch
  (`UTOS-S011`, spec 0.0.14). It is resolved exactly like an alias, which is what keeps it out of
  the dependency graph and so out of the cycle check that would otherwise reject recursive fan-out
  as a document depending on itself. The daemon never sees the word.
- Every `self` in a document is reported at once rather than one per build. An author fixing one
  site and rebuilding to discover the next is a worse experience than being told the whole list.

## [0.2.0] - 2026-08-12

### Added
- `utos cancel <execution-id>` — stop a running execution, with an optional `--reason` recorded on
  it. Cancellation is terminal and idempotent, and loses to a state the execution already reached:
  a run that has already completed or failed reports the daemon's `FAILED_PRECONDITION` rather than
  pretending to have cancelled it
- The `type` discriminator accepts **dot-separated paths** — `workflow.call`, `workflow.spawn`,
  `promise.all`, `promise.any`, `promise.race`, `promise.count` — following nested configuration
  oneofs. Keys are distributed across the messages on the path: `workflow` and `startActivity` are
  declared by the outer config so they stay outside, while `requiredCount` belongs to
  `promise.count` and `onEmitted` to `workflow.call`, so those nest one level deeper

### Changed
- **BREAKING**: bare `type: workflow` and `type: promise` are rejected with `UTOS-S007`. Neither
  says what the activity actually does — whether the caller awaits the child, or how the fan-out
  settles — and there is deliberately no default for either. The error lists the legal paths
- Bumped the Utos SDK packages to `0.0.12`
- The transform stays **entirely descriptor-driven**: paths, their legal spellings, and which
  message each authored key lands on are all derived from `WorkflowActivity.Descriptor`, so a new
  activity kind or mode added to the spec becomes authorable on an SDK bump with no code change
  here. `examples/order-fulfilment.yaml` moves to `promise.count` and `workflow.spawn`

## [0.1.0] - 2026-08-11

### Added
- `utos validate <file>` — resolves a workflow and its local dependencies, then checks the
  resulting bundle against the shared spec rules. Reports coded, addressable issues
  (`UTOS-S###` for source problems, with a line number; `UTOS-*` for bundle rule violations, with
  a path). `--json` emits the same information for scripts
- `utos inspect <file>` — shows the resolved dependency graph, the canonical identity each alias
  bound to, and the bundle's content digest
- Kubernetes-style source format: `apiVersion` / `kind` / `metadata` / `spec`, with each activity
  naming its kind via `type`. The transform is driven by the protobuf descriptor rather than a
  hard-coded table, so a new activity kind in the spec becomes authorable with no code change here
- YAML is read through YamlDotNet's node graph and resolved with the YAML 1.2 core schema, so
  `detached: true` reaches protobuf as a boolean and `requiredCount: 2` as a number while
  `duration: 30s` stays a string. Duplicate mapping keys are an error rather than last-wins
- Local (`./`, `../`) dependency resolution with cycle detection, diamond memoisation and
  identity-collision detection. Aliases are rewritten to canonical identities and
  `spec.dependencies` is emptied, so two builds of the same workflow produce the same digest
- `utos context create|use|ls|rm` — manage configured daemons in `~/.utos/config.json`
  (relocatable with `UTOS_CONFIG`). A command resolves its daemon in the order `--host`,
  `--context`, `UTOS_HOST`, current context
- `utos version` — CLI version plus the daemon's, via `GetHealth`. The quickest check that a
  context points at something real
- `utos load <file>` — resolve, validate and load a workflow, echoing the reference the daemon
  derived from its metadata
- `utos run <file|reference>` — schedule an execution. Given a file it resolves, validates, loads
  and then schedules, mirroring how `docker run` pulls when it needs to; given a reference it
  schedules something already loaded. `--start`, `--input` (JSON or `@file`), `--env KEY=VALUE`,
  `--env-file`, `--follow`
- `utos logs <execution-id>` — stream execution events, with `--tail`/`--after`, `--source`,
  `--category`, `--level` and `--follow`. Terminal status arrives in the stream rather than by
  polling, so `--follow` exits non-zero when the workflow fails
- Distinct exit codes for usage, validation failure, daemon error and workflow failure. Workflows
  are validated *before* the daemon is contacted, so a broken one fails identically whether or not
  a daemon is reachable, and with a better message than a flattened `InvalidArgument`

- Release pipeline producing self-contained NativeAOT binaries for `win-x64`, `linux-x64`,
  `linux-arm64`, `osx-x64` and `osx-arm64`, with `SHA256SUMS`, published to GitHub releases.
  NativeAOT cannot cross-compile, so each target builds on its own runner and smoke-tests the
  binary it just produced — which is what catches AOT failures that only appear at run time

### Notes
- There is no `utos build`. A `WorkflowBundle` is a wire payload rather than a distributable
  artifact, so resolution is a pipeline stage that `validate` and `load` drive, and `inspect` is
  the window onto it
- Registry dependency references are recognised and rejected with `UTOS-S010`; resolution awaits
  the OCI work in `DESIGN.md` §10
- `WorkflowReference.digest` is displayed by `inspect` but never sent as a guard on daemon calls:
  the digest format is still provisional upstream
- Built against the Utos SDK at `0.0.11`: `Utos.Workflow`, `Utos.Workflow.Validation` and
  `Utos.Daemon.Client`. `Utos.Daemon.Server` is deliberately absent — it defines the same
  `utos.daemon.v1` service types as the client and the two cannot coexist in one assembly
