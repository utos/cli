using Utos.Cli.Core.Source;
using Utos.Workflows.V1;
using Xunit;

namespace Utos.Cli.Tests;

/// <summary>
/// Covers the authored-source front end: the k8s-style <c>type</c> discriminator, YAML scalar
/// resolution, and the <c>UTOS-S###</c> errors.
/// </summary>
public class SourceFormatTests
{
    private const string Envelope = """
        apiVersion: utos.io/v1
        kind: Workflow
        metadata:
          name: sample
          version: "1.0.0"
        spec:
          activities:
        """;

    private static Workflow Parse(string activities) =>
        WorkflowLoader.Parse(Envelope + "\n" + Indent(activities), "sample.yaml");

    private static string Indent(string text) =>
        string.Join("\n", text.TrimEnd().Split('\n').Select(l => "    " + l));

    private static WorkflowSourceException ParseError(string activities) =>
        Assert.Throws<WorkflowSourceException>(() => Parse(activities));

    [Fact]
    public void Nests_configuration_under_the_field_named_by_type()
    {
        var workflow = Parse("""
            call:
              type: http
              method: GET
              url: https://api.example.com
              headers:
                accept: application/json
            """);

        var activity = workflow.Spec.Activities["call"];

        Assert.Equal(WorkflowActivity.ConfigOneofCase.Http, activity.ConfigCase);
        Assert.Equal("GET", activity.Http.Method);
        Assert.Equal("https://api.example.com", activity.Http.Url);
        Assert.Equal("application/json", activity.Http.Headers["accept"]);
    }

    [Fact]
    public void Keeps_transitions_at_activity_level()
    {
        // onSuccess/onFailure are fields of WorkflowActivity, not of the configuration, so the
        // transform must leave them where they are rather than sweeping them into `http`.
        var workflow = Parse("""
            call:
              type: http
              method: GET
              url: https://api.example.com
              onSuccess:
                - condition: "{{ output.ok }}"
                  transition:
                    name: end
              onFailure:
                - transition: { name: error }
            """);

        var activity = workflow.Spec.Activities["call"];

        Assert.Single(activity.OnSuccess);
        Assert.Single(activity.OnFailure);
        Assert.Equal("end", activity.OnSuccess[0].Transition.Name);
        Assert.Equal("{{ output.ok }}", activity.OnSuccess[0].Condition);
    }

    [Fact]
    public void Accepts_snake_case_spellings_too()
    {
        // proto3 JSON accepts both the proto field name and its lowerCamelCase json_name.
        var workflow = Parse("""
            call:
              type: http
              method: GET
              url: https://api.example.com
              on_success:
                - transition: { name: end }
            """);

        Assert.Single(workflow.Spec.Activities["call"].OnSuccess);
    }

    [Theory]
    [InlineData("timer", "duration: 30s")]
    [InlineData("promise", "mode: all\nbranches:\n  - name: a\n    target: { name: call }")]
    public void Supports_every_activity_kind(string type, string body)
    {
        var workflow = Parse($"""
            call:
              type: {type}
              {body.Replace("\n", "\n  ")}
            """);

        Assert.NotEqual(WorkflowActivity.ConfigOneofCase.None,
            workflow.Spec.Activities["call"].ConfigCase);
    }

    [Fact]
    public void Resolves_plain_scalars_with_the_yaml_core_schema()
    {
        // Type inference is load-bearing: `detached` must arrive as a JSON boolean and
        // `requiredCount` as a number, while `duration` must stay a string.
        var workflow = Parse("""
            sub:
              type: workflow
              workflow: emailer
              startActivity: send
              detached: true
            wait:
              type: timer
              duration: 90s
            """);

        Assert.True(workflow.Spec.Activities["sub"].Workflow.Detached);
        Assert.Equal(90, workflow.Spec.Activities["wait"].Timer.Duration.Seconds);
    }

    [Fact]
    public void Quoted_scalars_stay_strings()
    {
        // "1.0" would otherwise resolve as a number and fail to bind to a string field.
        var workflow = WorkflowLoader.Parse("""
            apiVersion: utos.io/v1
            kind: Workflow
            metadata:
              name: sample
              version: "1.0.0"
            spec:
              activities:
                call:
                  type: http
                  method: GET
                  url: https://api.example.com
            """, "sample.yaml");

        Assert.Equal("1.0.0", workflow.Metadata.Version);
    }

    [Fact]
    public void Reports_a_missing_type()
    {
        var error = ParseError("""
            call:
              method: GET
            """);

        Assert.Equal(SourceCodes.ActivityTypeInvalid, error.Issues[0].Code);
        Assert.Contains("http", error.Issues[0].Message);
    }

    [Fact]
    public void Reports_an_unknown_type_and_lists_the_legal_values()
    {
        var error = ParseError("""
            call:
              type: grpc
              url: https://api.example.com
            """);

        Assert.Equal(SourceCodes.ActivityTypeInvalid, error.Issues[0].Code);
        // The list is derived from the proto descriptor, not a hard-coded table.
        Assert.Contains("http, promise, timer, workflow", error.Issues[0].Message);
        Assert.True(error.Issues[0].Line > 0, "the issue should point at a line");
    }

    [Fact]
    public void Reports_every_bad_activity_in_one_pass()
    {
        var error = ParseError("""
            first:
              type: grpc
            second:
              type: soap
            """);

        Assert.Equal(2, error.Issues.Count);
        Assert.All(error.Issues, i => Assert.Equal(SourceCodes.ActivityTypeInvalid, i.Code));
    }

    [Fact]
    public void Rejects_unknown_fields()
    {
        // This is the "validate against the proto" step: a misspelled key is a mistake, not a
        // comment, and proto3-JSON parsing catches it for free.
        var error = ParseError("""
            call:
              type: http
              method: GET
              url: https://api.example.com
              retries: 3
            """);

        Assert.Equal(SourceCodes.DocumentMalformed, error.Issues[0].Code);
    }

    [Fact]
    public void Rejects_duplicate_keys()
    {
        // YAML 1.2 says duplicates are invalid, but last-wins would silently halve the activities.
        var error = ParseError("""
            call:
              type: http
              method: GET
              url: https://one.example.com
            call:
              type: http
              method: GET
              url: https://two.example.com
            """);

        Assert.Equal(SourceCodes.DuplicateKey, error.Issues[0].Code);
    }

    [Fact]
    public void Rejects_a_non_mapping_root()
    {
        var error = Assert.Throws<WorkflowSourceException>(
            () => WorkflowLoader.Parse("- just\n- a list\n", "sample.yaml"));

        Assert.Equal(SourceCodes.DocumentMalformed, error.Issues[0].Code);
    }

    [Fact]
    public void Rejects_an_empty_file()
    {
        var error = Assert.Throws<WorkflowSourceException>(
            () => WorkflowLoader.Parse(string.Empty, "sample.yaml"));

        Assert.Equal(SourceCodes.DocumentMalformed, error.Issues[0].Code);
    }
}
