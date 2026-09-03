# utos

The command-line interface for [Utos](https://github.com/utos/api) workflows.

The CLI resolves and validates workflows and talks to a daemon — the Docker split, where the
client builds and the daemon executes. It is the only component that reads authored source,
resolves dependencies and contacts registries; the daemon receives a fully-resolved bundle and
runs it.

## Install

Grab the binary for your platform from the [latest release](https://github.com/utos/cli/releases/latest)
and put it on your `PATH`. There is no runtime to install — the binaries are self-contained.

| Platform | Asset |
|---|---|
| Windows x64 | `utos-<version>-win-x64.zip` |
| Linux x64 / arm64 | `utos-<version>-linux-x64.tar.gz`, `…-linux-arm64.tar.gz` |
| macOS Apple silicon / Intel | `utos-<version>-osx-arm64.tar.gz`, `…-osx-x64.tar.gz` |

`SHA256SUMS` is published alongside them:

```bash
sha256sum -c SHA256SUMS --ignore-missing
```

## Commands

| Command | Does |
|---------|------|
| `utos validate <file>` | Resolve a workflow and its dependencies, then check the bundle against the spec rules |
| `utos inspect <file>` | Show the resolved dependency graph, canonical identities and content digest |
| `utos context create\|use\|ls\|rm` | Manage the daemons this CLI knows about |
| `utos version` | CLI version, and the daemon's if one can be reached |
| `utos load <file>` | Resolve, validate and load a workflow onto a daemon |
| `utos run <file\|reference>` | Schedule an execution, optionally loading the file first |
| `utos logs <execution-id>` | Stream an execution's events |

```bash
utos context create local http://localhost:5164
utos run ./examples/hello.yaml --start greet \
    --input '{"name":"world"}' \
    --env API_BASE=https://api.dev \
    --follow
```

A command picks its daemon from `--host`, then `--context`, then `UTOS_HOST`, then the current
context. Configuration lives in `~/.utos/config.json`, relocatable with `UTOS_CONFIG`.

Workflows are validated before the daemon is contacted, so a broken one fails the same way
whether or not a daemon is running.

There is deliberately **no `utos build`**. A `WorkflowBundle` is a wire payload, not a
distributable artifact — what gets published to a registry is the source graph, and the flattened
bundle only ever exists on its way to a daemon. Resolution is a pipeline stage that `validate` and
`load` both drive; `inspect` is the window onto it.

## Writing a workflow

Workflows follow the Kubernetes manifest convention — `apiVersion`, `kind`, `metadata`, `spec`,
and nothing else at the top level. Each activity names its kind with `type`:

```yaml
apiVersion: utos.io/v1
kind: Workflow
metadata:
  name: hello
  version: "1.0.0"
spec:
  activities:
    greet:
      type: http
      method: GET
      url: "{{ env.API_BASE }}/hello/{{ input.name }}"
      onSuccess:
        - condition: "{{ output.ok }}"
          transition: { name: end }
```

The legal `type` values are derived from the protobuf descriptor rather than hard-coded, so a new
activity kind in the spec becomes authorable as soon as the SDK package is bumped. See
[`workflow-source-format.md`](https://github.com/utos/api/blob/main/docs/workflow-source-format.md)
for the normative mapping and [`examples/`](examples) for working files.

## Errors

Source problems carry `UTOS-S###` codes and point at a line; bundle rule violations carry the
`UTOS-*` codes from
[`workflow-validation.md`](https://github.com/utos/api/blob/main/docs/workflow-validation.md) and
an addressable path:

```
UTOS-T003 workflows["acme/greet:1.0.0"].spec.activities["send"].onSuccess[0].transition.name
  Transition target 'notify' is neither an activity in this workflow nor a reserved terminal keyword (end, error).
```

`--json` on `validate` emits the same information for scripts.

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Unclassified error |
| 2 | Usage error |
| 3 | The workflow was read but is invalid |
| 4 | The daemon could not be reached, or refused the request |
| 5 | The workflow ran and failed |

## Building

Requires the .NET 10 SDK. NativeAOT publishing additionally needs a C++ toolchain — on Windows,
Visual Studio with the MSVC workload, with `vswhere.exe` reachable
(`C:\Program Files (x86)\Microsoft Visual Studio\Installer` on `PATH`).

```bash
dotnet build Utos.Cli.slnx -c Release
dotnet test  Utos.Cli.slnx -c Release
dotnet publish src/Utos.Cli/Utos.Cli.csproj -c Release -r win-x64
```

## Design notes

The CLI ships as **NativeAOT single-file binaries**, which constrains its dependencies: anything
resolving types by reflection at run time is out. That is why parsing uses `System.CommandLine`
rather than `Spectre.Console.Cli`, why YAML is read through YamlDotNet's node graph rather than
its object deserializer, why configuration is JSON with source-generated serialization, and why
console output is a few dozen lines of ANSI rather than a rendering library.

Validation is not implemented here. It lives in `Utos.Workflow.Validation`, shared with the
daemon and driven by the conformance fixtures in `utos/api`, so a workflow one tool accepts cannot
be rejected by another.

There is no `utos build`. A bundle is a wire payload rather than a distributable artifact, so
resolution is a stage inside `validate` and `load`, and `inspect` shows the result — nothing writes
a bundle to disk.

`src/Utos.Cli` owns the console — commands, output, exit codes — and `src/Utos.Cli.Core` owns the
pipeline and the daemon client and knows nothing about it. The split is load-bearing rather than
tidy: everything in `Core` has to be callable from a test with no terminal, so it returns values
and throws instead of writing to `Console`.

Two things that will bite a contributor otherwise. The protobuf C# namespace is
`Utos.Workflows.V1` — **plural** — while the wire package is `utos.workflow.v1`, singular. A
singular namespace would shadow the `Workflow` message type for anything under `Utos.*`, and a
using-alias cannot fix it, because C# resolves simple names through enclosing namespaces before
using-directives. And a test project must not reference `Utos.Daemon.Server` alongside
`Utos.Daemon.Client`: the two define the same gRPC service types and cannot coexist in one
assembly.
