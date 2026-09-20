# Changelog

All notable changes to the Utos CLI are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

**Version parity across the Utos repos: the minor is the contract, the patch is
this CLI's own.** `0.19.x` here means *implements spec 0.19*, the same way it
does in `utos/dapr-daemon` and `utos/sdk-dotnet`. If the spec reaches `0.20` and
this CLI has not implemented it, it stays at `0.19.x` — which is then a true
statement about what it supports rather than a number a pipeline invented.

## [0.19.0]

### Changed

- **Version parity: the unreleased `0.4.0` becomes `0.19.0`** (spec `0.19.0`). The CLI was heading
  for `0.4.0` while the spec was at `0.0.18` and `utos/dapr-daemon` at `0.1.0`, so no version
  number said which spec this CLI spoke. From here the minor is the contract and the patch is this
  repo's own, so `0.19.1` is a CLI fix against the same spec and `0.20.0` follows a spec that
  moved. Nothing is skipped: the CLI adopts the spec's line, which had eighteen releases behind it,
  and `0.19.0` is the smallest number that lets every Utos repo move forward onto it — this one was
  already past `0.1.0`, and neither a package registry nor a git tag can be reused

- **The source format is read from `Utos.Workflow.Source` rather than from a copy here.** The
  mapping onto `utos.workflow.v1.Workflow` is normative — the spec defines it and
  `api/conformance/source/` exists to prove two front ends agree on it — and it lived in this one
  front end. The hub's upload path reads the same documents, so the alternative was a second
  implementation of it. **1089 lines deleted against 11 added**, and `YamlDotNet` goes with them:
  reading YAML was the mapping's business, not the CLI's. The source conformance corpus moves too,
  and now runs in `sdk-dotnet` against the copy the release pipeline vendors from the spec tag
  rather than here against a hand-vendored fixture that could drift from it
- **NativeAOT is unaffected, which was the risk worth checking.** The publish still reports zero
  `IL2026`, `IL3050` and `IL3053`. The constraint now holds upstream as well: `sdk-dotnet` targets
  `net10.0` since `0.0.18.2`, so `IsAotCompatible` works there and the analyzer runs on the
  assemblies this binary links — which it could not while they targeted `netstandard2.0`
- Utos SDK pins `0.0.17.1` → `0.0.18.2`, which also brings the schema rules the validator gained
  for spec `0.0.18` (`UTOS-H001`–`H014`) and the short-form compiler

### Added

- **`utos inspect` shows the workflow's contract** (spec `0.0.18`): `spec.env`, `spec.output`,
  `spec.emits`, and the input each activity accepts. Every activity that declares one, not just a
  chosen entry point — a run is scheduled with a start activity and its input goes to *that*
  activity, so a document with three entry points has three input shapes. Rendered in the short
  form's spelling rather than as the JSON Schema it compiled to: `watermark?: string | null` is
  what the author wrote, where the bundle holds `{"type": ["string", "null"]}`. A workflow that
  declares nothing prints nothing, since a row reading "none" would suggest a document had said
  something it did not
- **`utos validate` reports the schema short form's own defects** — `UTOS-S012` (one property
  declared both required and optional, `x` alongside `x?`), `UTOS-S013` (a type outside the
  registry) and `UTOS-S014` (a constraint that does not apply to the declared type) — with a file
  and a line, like every other source-format issue
- **A bare `error` re-raises the failure being handled** (spec `0.0.17`). `- error`, `error:`, `error: ~` and the flow-style `{ condition: x, error }` in a rule map to an empty `error`, the same way a bare `return` maps to an empty `result`. On `onFailure` it fails the path with the failure being handled, as it is, so a workflow can forward a sub-workflow's failure without renaming it. Elsewhere the validator refuses it (`UTOS-T005`). The Utos SDK pins are bumped `0.0.16.1` → `0.0.17.1`, the first validator that accepts it, and the source conformance corpus is vendored at `v0.0.17`

## [0.3.0] - 2026-09-16

### Added
- **`utos workflow ls` and `utos workflow rm`**: see and remove what is loaded on a daemon.
  `rm` without a version removes every loaded version, as the daemon's `UnloadWorkflow` defines,
  and a reference that matches nothing is an error rather than a quiet "removed".
- **`utos execution ls` and `utos execution output`**: list runs, newest first, and read what one
  produced. `output` reads the durable output stream, so a run's result stays readable after the
  in-memory log buffer `utos logs` follows has been evicted.
- **A second example, `examples/refund-request.yaml`**, with its `shared/settle-refund.yaml` sub-workflow: a model reads a customer's request and the file decides whether money moves, checking the amount against the real order and an auto-approval limit before calling payments. `examples/mocks/` holds WireMock stubs for the three services it calls, `refund-request.env` points at them, and `ticket.json` is an input for `--input @ticket.json` — enough to run it end to end against a local daemon with no real services.
- List output aligns on visible width, ignoring colour codes, so a coloured status column does
  not push the columns after it out of line.

### Fixed
- **Output is UTF-8 when piped or redirected.** On Windows a redirected console fell back to a legacy code page, which dropped the arrow in a transition log line and degraded inspect's tree characters and any non-ASCII text in a workflow description. The console is now UTF-8, without a BOM, on every platform.
- **`--input @file.json` works.** `System.CommandLine` expands any `@`-prefixed argument as a response file by default, so the file was split into command-line tokens before `--input` saw it and the command failed with a usage error. The helper that reads the file was unit-tested on its own, which is why nothing caught it. Response-file expansion is now off; nothing in this CLI uses it, and `@` belongs to `--input`.

### Changed
- **`utos logs` and `utos run --follow` print less and highlight more.** A line is now time,
  level and message. The daemon's category — the .NET type that logged the event — is no longer
  printed; it is an implementation detail of one daemon, and stays on the wire for `--category`.
  The source is printed only when a sub-workflow wrote the line. With colour on, activity names
  are bold cyan, durations dimmed, and `completed` / `failed` take their status colour.
  Highlighting reads the message text, which is not a contract, so a line no pattern matches prints
  exactly as sent — the styling never carries meaning a reader needs.
- **The terminal frame is no longer printed as a log line.** It read `Execution completed` directly
  under the executor's own completion line, which already says so with a duration. The frame still
  ends the stream and sets the exit code, and a failure still prints its error.
- **`FORCE_COLOR` and `CLICOLOR_FORCE` turn colour on for redirected output**, for a CI log that
  renders ANSI or a terminal recording. `NO_COLOR` still wins.
- **Adopts specs 0.0.15 and 0.0.16: JavaScript expressions, `return`, `error`.** A condition is a
  bare JavaScript expression (`output.ok`, not `{{ output.ok }}`) and `{{ }}` interpolates one
  into text; the shared validator checks the grammar at `validate` and `load`, so a Scriban
  document fails there by name rather than at run time. A rule ends the run with `return` — with
  a value, or bare to end with none — and fails it with `error` (`code`, `message`, `details`);
  the `end` and `error` transition targets are gone and a document still naming them fails
  `UTOS-T003`. **`return` is `result` on the wire**, the mapping the source-format spec makes
  normative in 0.0.16: `return` is renamed, a bare `return` becomes an empty struct (proto3 JSON
  would read `"result": null` as no action at all), and `result` in a source document is refused
  (`UTOS-S009`) so authored files have one spelling. Applied to `onSuccess`, `onFailure` and
  `onEmitted` alike. `SourceIssue` gains a `Path` in the validation corpus's notation where the
  problem can be addressed that way. Bumped the Utos SDK packages to `0.0.16.1`
- **The source-format conformance corpus runs in the tests** (`Fixtures/conformance/source`,
  vendored from `utos/api` v0.0.16): every document and the `Workflow` it must map to, or the
  `UTOS-S###` it must produce. It is what makes "does a second front-end read a document the way
  this one does" answerable
- Examples and the README are written in the 0.0.16 form
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
