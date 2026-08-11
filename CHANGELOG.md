# Changelog

All notable changes to the Utos CLI are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0]

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
