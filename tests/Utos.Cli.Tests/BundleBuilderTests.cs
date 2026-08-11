using Utos.Cli.Core.Build;
using Utos.Cli.Core.Source;
using Utos.Workflows.V1;
using Utos.Workflows.V1.Validation;
using Xunit;

namespace Utos.Cli.Tests;

/// <summary>
/// Covers dependency resolution and the source → bundle transformation: aliases become canonical
/// identities, <c>spec.dependencies</c> is emptied, and the graph's failure modes are reported
/// with their <c>UTOS-S###</c> codes.
/// </summary>
public class BundleBuilderTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "utos-cli-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Leaf(string name, string version) => $"""
        apiVersion: utos.io/v1
        kind: Workflow
        metadata:
          name: {name}
          version: "{version}"
          namespace: acme
        spec:
          activities:
            send:
              type: http
              method: POST
              url: https://mail.example.com/send
        """;

    [Fact]
    public void Rewrites_aliases_to_canonical_identities_and_empties_dependencies()
    {
        Write("send-email.yaml", Leaf("send-email", "2.1.0"));
        var entry = Write("root.yaml", """
            apiVersion: utos.io/v1
            kind: Workflow
            metadata:
              name: root
              version: "1.0.0"
              namespace: acme
            spec:
              dependencies:
                emailer: ./send-email.yaml
              activities:
                notify:
                  type: workflow
                  workflow: emailer
                  startActivity: send
            """);

        var result = BundleBuilder.Build(entry);

        Assert.Equal("acme/root:1.0.0", result.Bundle.EntryPoint);
        Assert.Equal(2, result.Bundle.Workflows.Count);

        var notify = result.Bundle.Workflows["acme/root:1.0.0"].Spec.Activities["notify"];
        Assert.Equal("acme/send-email:2.1.0", notify.Workflow.Workflow);

        // Emptied so two builds of the same logical workflow hash identically (UTOS-B007).
        Assert.Empty(result.Bundle.Workflows["acme/root:1.0.0"].Spec.Dependencies);

        // And the result must satisfy the shared rules.
        Assert.True(WorkflowBundleValidator.Validate(result.Bundle).IsValid);
    }

    [Fact]
    public void Records_the_dependency_tree()
    {
        Write("send-email.yaml", Leaf("send-email", "2.1.0"));
        var entry = Write("root.yaml", """
            apiVersion: utos.io/v1
            kind: Workflow
            metadata:
              name: root
              version: "1.0.0"
            spec:
              dependencies:
                emailer: ./send-email.yaml
              activities:
                notify:
                  type: workflow
                  workflow: emailer
                  startActivity: send
            """);

        var tree = BundleBuilder.Build(entry).Tree;

        Assert.Null(tree.Alias);
        Assert.Equal("root:1.0.0", tree.Identity);
        var child = Assert.Single(tree.Dependencies);
        Assert.Equal("emailer", child.Alias);
        Assert.Equal("acme/send-email:2.1.0", child.Identity);
    }

    [Fact]
    public void Reports_a_missing_dependency_file()
    {
        var entry = Write("root.yaml", Root("emailer: ./nope.yaml"));

        var error = Assert.Throws<WorkflowSourceException>(() => BundleBuilder.Build(entry));

        Assert.Equal(SourceCodes.DependencyFileUnreadable, error.Issues[0].Code);
    }

    [Fact]
    public void Reports_a_registry_reference_as_unsupported()
    {
        var entry = Write("root.yaml", Root("emailer: registry.utos.dev/acme/send-email:1.0.0"));

        var error = Assert.Throws<WorkflowSourceException>(() => BundleBuilder.Build(entry));

        Assert.Equal(SourceCodes.RegistryUnsupported, error.Issues[0].Code);
    }

    [Fact]
    public void Reports_an_activity_referencing_an_undeclared_alias()
    {
        var entry = Write("root.yaml", """
            apiVersion: utos.io/v1
            kind: Workflow
            metadata:
              name: root
              version: "1.0.0"
            spec:
              activities:
                notify:
                  type: workflow
                  workflow: mystery
                  startActivity: send
            """);

        var error = Assert.Throws<WorkflowSourceException>(() => BundleBuilder.Build(entry));

        Assert.Equal(SourceCodes.DependencyAliasUnknown, error.Issues[0].Code);
    }

    [Fact]
    public void Detects_a_dependency_cycle()
    {
        Write("a.yaml", Cycle("a", "./b.yaml"));
        Write("b.yaml", Cycle("b", "./a.yaml"));

        var error = Assert.Throws<WorkflowSourceException>(
            () => BundleBuilder.Build(Path.Combine(_directory, "a.yaml")));

        Assert.Equal(SourceCodes.DependencyCycle, error.Issues[0].Code);
    }

    [Fact]
    public void Resolves_a_diamond_once_without_reporting_a_cycle()
    {
        // Both branches reach the same leaf. Memoising before the cycle check is what keeps this
        // from being mistaken for a loop.
        Write("leaf.yaml", Leaf("leaf", "1.0.0"));
        Write("left.yaml", Cycle("left", "./leaf.yaml"));
        Write("right.yaml", Cycle("right", "./leaf.yaml"));
        var entry = Write("root.yaml", """
            apiVersion: utos.io/v1
            kind: Workflow
            metadata:
              name: root
              version: "1.0.0"
            spec:
              dependencies:
                l: ./left.yaml
                r: ./right.yaml
              activities:
                one:
                  type: workflow
                  workflow: l
                  startActivity: call
                two:
                  type: workflow
                  workflow: r
                  startActivity: call
            """);

        var result = BundleBuilder.Build(entry);

        // root, left, right, leaf — the leaf appears once.
        Assert.Equal(4, result.Bundle.Workflows.Count);
        Assert.Contains("acme/leaf:1.0.0", result.Bundle.Workflows.Keys);
    }

    private static string Root(string dependency) => $"""
        apiVersion: utos.io/v1
        kind: Workflow
        metadata:
          name: root
          version: "1.0.0"
        spec:
          dependencies:
            {dependency}
          activities:
            notify:
              type: workflow
              workflow: emailer
              startActivity: send
        """;

    private static string Cycle(string name, string reference) => $"""
        apiVersion: utos.io/v1
        kind: Workflow
        metadata:
          name: {name}
          version: "1.0.0"
        spec:
          dependencies:
            other: {reference}
          activities:
            call:
              type: workflow
              workflow: other
              startActivity: send
        """;
}
