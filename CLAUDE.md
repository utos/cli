# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What This Is

The `utos` CLI. It reads authored workflow YAML, resolves dependencies, validates the result, and
talks to a daemon over gRPC. Per `DESIGN.md` in the workspace root: **the CLI builds, the daemon
executes**. The CLI is the only component that reads source, resolves references, contacts
registries and handles auth.

## Layout

```
src/Utos.Cli/        the executable — commands, output, exit codes. Knows about the console.
src/Utos.Cli.Core/   the pipeline and daemon client. Knows nothing about the console.
tests/Utos.Cli.Tests/
examples/            working workflows, used by docs and by hand
```

The split is load-bearing: anything in `Utos.Cli.Core` must be callable from a test without a
terminal, so it returns values and throws, and never writes to `Console`.

## Rules

1. **NativeAOT is a hard constraint.** The CLI ships as single-file native binaries, so no
   dependency may resolve types by reflection at run time. `IsAotCompatible` is set tree-wide so
   violations surface at build time, not publish time. This is why parsing is `System.CommandLine`
   and not `Spectre.Console.Cli`, why YAML goes through YamlDotNet's *node graph* and never its
   object deserializer, and why JSON output is written with `Utf8JsonWriter` rather than a
   serializer. Before adding a package, check what it does at run time.
2. **Validation is not implemented here.** Bundle rules live in `Utos.Workflow.Validation`, shared
   with the daemon and pinned by the conformance fixtures in `utos/api`. A rule that belongs to
   the spec goes there and gets a code in `api/docs/workflow-validation.md`; only source-format
   problems (`UTOS-S###`) belong in this repo.
3. **The source format is specified, not invented.** `api/docs/workflow-source-format.md` is
   normative. The activity `type` transform is derived from the protobuf descriptor — never
   hard-code the list of activity kinds.
4. **There is no `utos build`.** A bundle is a wire payload, not an artifact. Resolution is a
   stage inside `validate` and `load`; `inspect` shows the result. Do not add a command that
   writes a bundle to disk.
5. **Never send a computed digest as a guard** on daemon calls. The canonical digest format is
   provisional upstream and has no golden vectors yet. Display it; do not enforce it.

## Naming

The protobuf C# namespace is `Utos.Workflows.V1` (**plural**) while the wire package is
`utos.workflow.v1` (**singular**). This is deliberate: a singular namespace would shadow the
`Workflow` message type for anything under `Utos.*`, and a using-alias cannot fix that — C#
resolves simple names through enclosing namespaces before using-directives. Do not "correct" the
plural, and do not change the wire package.

## Conventions

- Branching: `main` (releases), `dev` (integration), feature branches off `dev`. Squash into
  `dev`, merge commit into `main`.
- `CHANGELOG.md` is the source of truth for versioning (Keep a Changelog).
- [Conventional Commits](https://www.conventionalcommits.org/): `type(scope): description`.
  Scopes: `source`, `build`, `validate`, `daemon`, `context`, `output`.
- **Do not add `Co-Authored-By` lines for AI assistance.**

## Testing

xunit with plain `Assert`, matching the sibling repos — no FluentAssertions, no mocking library.
Daemon-facing code is tested by substituting a fake `CallInvoker`; do **not** reference
`Utos.Daemon.Server` from a project that also reaches `Utos.Daemon.Client`, since the two define
the same gRPC service types and cannot coexist in one assembly.
