using System.Text.Json;
using Google.Protobuf;
using Utos.Cli.Core.Source;
using Utos.Workflows.V1;
using Xunit;

namespace Utos.Cli.Tests;

/// <summary>
/// Runs the source-format conformance corpus of <c>utos/api</c> (<c>conformance/source/</c>),
/// vendored under <c>Fixtures/conformance/source</c> with the tag it came from in its README.
/// Each case is a document and either the <c>Workflow</c> it must map to — the document-level
/// mapping only, dependencies as authored — or the <c>UTOS-S###</c> issues it must produce. This
/// is what says a second front-end reads a document the way this one does.
/// </summary>
public class SourceConformanceTests
{
    public static TheoryData<string> Cases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var file in Directory.EnumerateFiles(CorpusRoot, "*.yaml").OrderBy(f => f, StringComparer.Ordinal))
                data.Add(Path.GetFileNameWithoutExtension(file));
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_document_maps_as_the_corpus_expects(string name)
    {
        var source = File.ReadAllText(Path.Combine(CorpusRoot, name + ".yaml"));
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusRoot, name + ".expected.json")));

        if (expected.RootElement.TryGetProperty("workflow", out var workflow))
        {
            // Compared as messages, not as text: proto3 JSON has one meaning per document but
            // several spellings, and message equality is the meaning.
            var expectedWorkflow = JsonParser.Default.Parse<Workflow>(workflow.GetRawText());

            var actual = WorkflowLoader.Parse(source, name + ".yaml");

            Assert.Equal(expectedWorkflow, actual);
            return;
        }

        var exception = Assert.Throws<WorkflowSourceException>(() => WorkflowLoader.Parse(source, name + ".yaml"));

        var expectedIssues = expected.RootElement.GetProperty("issues").EnumerateArray()
            .Select(i => (Code: i.GetProperty("code").GetString()!, Path: i.GetProperty("path").GetString()!))
            .ToList();

        Assert.Equal(expectedIssues.Select(i => i.Code), exception.Issues.Select(i => i.Code));

        // The path is asserted where this implementation can address the problem; one the YAML
        // or protobuf parser reported carries a line instead.
        foreach (var (expectedIssue, actualIssue) in expectedIssues.Zip(exception.Issues))
        {
            if (actualIssue.Path != null)
                Assert.Equal(expectedIssue.Path, actualIssue.Path);
        }
    }

    [Fact]
    public void The_corpus_is_present()
    {
        Assert.True(Directory.Exists(CorpusRoot), $"corpus not found: {CorpusRoot}");
        Assert.NotEmpty(Directory.EnumerateFiles(CorpusRoot, "*.yaml"));
    }

    private static string CorpusRoot { get; } =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "conformance", "source");
}
