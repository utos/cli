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
                - condition: "output.ok"
                  transition:
                    name: next
              onFailure:
                - error: { code: CALL_FAILED, message: "{{ error.message }}" }
            next:
              type: timer
              duration: 1s
            """);

        var activity = workflow.Spec.Activities["call"];

        Assert.Single(activity.OnSuccess);
        Assert.Single(activity.OnFailure);
        Assert.Equal("next", activity.OnSuccess[0].Transition.Name);
        Assert.Equal("output.ok", activity.OnSuccess[0].Condition);
        Assert.Equal("CALL_FAILED", activity.OnFailure[0].Error.Code);
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
                - return: { ok: true }
            """);

        Assert.Single(workflow.Spec.Activities["call"].OnSuccess);
        Assert.True(workflow.Spec.Activities["call"].OnSuccess[0].Result.Fields["ok"].BoolValue);
    }

    [Fact]
    public void Return_is_result_on_the_wire()
    {
        // The source format spells the action `return`; the wire keeps `result` because `return`
        // is a reserved word in generated code for several languages.
        var workflow = Parse("""
            call:
              type: http
              method: GET
              url: https://api.example.com
              onSuccess:
                - condition: "output.ok"
                  return:
                    greeted: "{{ output.name }}"
            """);

        var rule = workflow.Spec.Activities["call"].OnSuccess[0];

        Assert.Equal(TransitionRule.ActionOneofCase.Result, rule.ActionCase);
        Assert.Equal("{{ output.name }}", rule.Result.Fields["greeted"].StringValue);
    }

    [Theory]
    [InlineData("- return")]
    [InlineData("- return:")]
    [InlineData("- return: ~")]
    [InlineData("- return: null")]
    [InlineData("- condition: \"output.done\"\n  return:")]
    [InlineData("- { condition: \"output.done\", return }")]
    public void A_bare_return_is_an_empty_result(string rule)
    {
        // proto3 JSON would read `"result": null` as unset — a rule with no action — so "no value"
        // has to reach the bundle as an empty struct, which ends the path with no value.
        var workflow = Parse($"""
            call:
              type: http
              method: GET
              url: https://api.example.com
              onSuccess:
                {rule.Replace("\n", "\n    ")}
            """);

        var parsed = workflow.Spec.Activities["call"].OnSuccess[0];

        Assert.Equal(TransitionRule.ActionOneofCase.Result, parsed.ActionCase);
        Assert.Empty(parsed.Result.Fields);
    }

    [Fact]
    public void Maps_return_inside_onEmitted_too()
    {
        // The same rule shape on the consumer's emission rules, which are authored flat beside
        // the call configuration and nested afterwards.
        var workflow = Parse("""
            watch:
              type: workflow.call
              workflow: mailbox
              startActivity: poll
              onEmitted:
                - condition: "output.done"
                  return:
                - error: { code: BAD_VALUE }
            """);

        var rules = workflow.Spec.Activities["watch"].Workflow.Call.OnEmitted;

        Assert.Equal(EmissionRule.ActionOneofCase.Result, rules[0].ActionCase);
        Assert.Empty(rules[0].Result.Fields);
        Assert.Equal(EmissionRule.ActionOneofCase.Error, rules[1].ActionCase);
    }

    [Fact]
    public void Rejects_result_in_a_source_document()
    {
        // A valid wire field, so protobuf would accept it silently — refused here so an authored
        // document has one spelling, and the wire name never leaks into files.
        var error = ParseError("""
            call:
              type: http
              method: GET
              url: https://api.example.com
              onSuccess:
                - result: { ok: true }
            """);

        var issue = Assert.Single(error.Issues);
        Assert.Equal(SourceCodes.DocumentMalformed, issue.Code);
        Assert.Equal("spec.activities[\"call\"].onSuccess[0].result", issue.Path);
        Assert.Contains("return", issue.Message);
        Assert.True(issue.Line > 0);
    }

    [Theory]
    [InlineData("timer", "duration: 30s")]
    [InlineData("promise.all", "branches:\n  - name: a\n    workflow: pricer\n    startActivity: quote")]
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

    [Theory]
    [InlineData("workflow.call")]
    [InlineData("workflow.spawn")]
    public void Nests_a_dotted_type_under_both_of_its_segments(string type)
    {
        // A two-segment path distributes its keys: `workflow`/`startActivity` are declared by
        // WorkflowActivityConfig, so they stay at the outer level, while the mode message gets
        // whatever it declares — here nothing, so it serializes as the bare discriminator.
        var workflow = Parse($"""
            sub:
              type: {type}
              workflow: emailer
              startActivity: send
            """);

        var config = workflow.Spec.Activities["sub"].Workflow;
        var mode = type.Split('.')[1];

        Assert.Equal("emailer", config.Workflow);
        Assert.Equal("send", config.StartActivity);
        Assert.Equal(mode, config.ModeCase.ToString().ToLowerInvariant());
    }

    [Fact]
    public void Places_a_key_on_the_message_along_the_path_that_declares_it()
    {
        // `branches` belongs to PromiseActivityConfig and `requiredCount` to PromiseCountConfig,
        // so one authored mapping lands on two different levels.
        var workflow = Parse("""
            fan-out:
              type: promise.count
              requiredCount: 2
              branches:
                - name: a
                  workflow: self
                  startActivity: fan-out
            """);

        var promise = workflow.Spec.Activities["fan-out"].Promise;

        Assert.Single(promise.Branches);
        Assert.Equal(PromiseActivityConfig.CompletionOneofCase.Count, promise.CompletionCase);
        Assert.Equal(2, promise.Count.RequiredCount);
    }

    [Fact]
    public void Accepts_onEmitted_in_both_spellings_inside_a_call()
    {
        // onEmitted is declared by CallActivityConfig, so it must reach the inner message even
        // though the author writes it flat beside workflow/startActivity. The rule's own action
        // nests one level further, which is what lets a rule carry a transition or a result
        // instead.
        var workflow = Parse("""
            watch:
              type: workflow.call
              workflow: mailbox
              startActivity: poll
              on_emitted:
                - condition: "output.done"
                  transition: { name: wrap-up }
                - handle:
                    workflow: ingester
                    startActivity: ingest
            """);

        var call = workflow.Spec.Activities["watch"].Workflow.Call;

        Assert.Equal(2, call.OnEmitted.Count);
        Assert.Equal(EmissionRule.ActionOneofCase.Transition, call.OnEmitted[0].ActionCase);
        Assert.Equal("wrap-up", call.OnEmitted[0].Transition.Name);
        Assert.Equal(EmissionRule.ActionOneofCase.Handle, call.OnEmitted[1].ActionCase);
        Assert.Equal("ingester", call.OnEmitted[1].Handle.Workflow);
        Assert.Equal("ingest", call.OnEmitted[1].Handle.StartActivity);
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("promise")]
    public void Rejects_a_type_path_that_stops_short_of_a_mode(string type)
    {
        // Bare `workflow` says nothing about whether the caller awaits the child, and bare
        // `promise` says nothing about how it settles. There is deliberately no default.
        var error = ParseError($"""
            sub:
              type: {type}
              workflow: emailer
              startActivity: send
            """);

        Assert.Equal(SourceCodes.ActivityTypeInvalid, error.Issues[0].Code);
        Assert.Contains($"unknown type '{type}'", error.Issues[0].Message);
    }

    [Fact]
    public void Resolves_plain_scalars_with_the_yaml_core_schema()
    {
        // Type inference is load-bearing: `requiredCount` must arrive as a JSON number while
        // `duration` must stay a string.
        var workflow = Parse("""
            fan-out:
              type: promise.count
              requiredCount: 2
              branches:
                - name: a
                  workflow: self
                  startActivity: wait
            wait:
              type: timer
              duration: 90s
            """);

        Assert.Equal(2, workflow.Spec.Activities["fan-out"].Promise.Count.RequiredCount);
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
        Assert.Contains(
            "http, promise.all, promise.any, promise.count, promise.race, timer, workflow.call, "
            + "workflow.spawn",
            error.Issues[0].Message);
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
