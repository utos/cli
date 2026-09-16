using YamlDotNet.RepresentationModel;

namespace Utos.Cli.Core.Source;

/// <summary>
/// Rewrites a rule's <c>return</c> action into the wire's <c>result</c>, as
/// <c>api/docs/workflow-source-format.md</c> § Mapping specifies.
/// <para>
/// The wire keeps the name <c>result</c> because <c>return</c> is a reserved word in several
/// target languages — a generated <c>msg.return</c> is a syntax error in Python — and the source
/// format spells it <c>return</c> because that is what ending a path with a value is. A bare
/// <c>return</c> — <c>- return</c>, <c>- return:</c>, <c>- return: ~</c> — ends the path with no
/// value and becomes an empty struct: proto3 JSON reads <c>"result": null</c> as <em>unset</em>,
/// which would be a rule with no action at all. <c>result</c> in a source document is refused
/// (<c>UTOS-S009</c>), so the two spellings never coexist and the wire name never leaks into
/// authored files.
/// </para>
/// </summary>
internal static class RuleTransform
{
    private const string SourceKey = "return";
    private const string WireKey = "result";

    /// <summary>The activity-level keys whose values are rule lists, in both spellings proto3 JSON accepts.</summary>
    private static readonly IReadOnlyDictionary<string, string> RuleLists = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["onSuccess"] = "onSuccess", ["on_success"] = "onSuccess",
        ["onFailure"] = "onFailure", ["on_failure"] = "onFailure",
        // Declared by CallActivityConfig, but authored flat beside `type: workflow.call`, so it
        // is an activity-level key here; ActivityTransform nests it afterwards.
        ["onEmitted"] = "onEmitted", ["on_emitted"] = "onEmitted",
    };

    /// <summary>
    /// Returns a copy of <paramref name="activity"/> with every rule list rewritten. Issues are
    /// accumulated rather than thrown so one run reports every bad rule.
    /// </summary>
    public static YamlMappingNode Rewrite(YamlMappingNode activity, string activityName, string file,
        List<SourceIssue> issues)
    {
        var rewritten = new YamlMappingNode();
        foreach (var (key, value) in activity.Children)
        {
            if (RuleLists.TryGetValue(YamlJson.Key(key), out var canonical) && value is YamlSequenceNode rules)
            {
                var path = $"spec.activities[\"{activityName}\"].{canonical}";
                rewritten.Add(key, RewriteRules(rules, path, file, issues));
            }
            else
            {
                rewritten.Add(key, value);
            }
        }

        return rewritten;
    }

    private static YamlSequenceNode RewriteRules(YamlSequenceNode rules, string path, string file,
        List<SourceIssue> issues)
    {
        var rewritten = new YamlSequenceNode();
        var index = 0;
        foreach (var rule in rules.Children)
        {
            rewritten.Add(RewriteRule(rule, $"{path}[{index}]", file, issues));
            index++;
        }

        return rewritten;
    }

    private static YamlNode RewriteRule(YamlNode rule, string path, string file, List<SourceIssue> issues)
    {
        // `- return`: a bare scalar item, the whole rule. The only scalar a rule may be.
        if (rule is YamlScalarNode { Value: SourceKey, Style: YamlDotNet.Core.ScalarStyle.Plain })
            return new YamlMappingNode { { new YamlScalarNode(WireKey), new YamlMappingNode() } };

        if (rule is not YamlMappingNode mapping)
            return rule;

        var rewritten = new YamlMappingNode();
        foreach (var (key, value) in mapping.Children)
        {
            switch (YamlJson.Key(key))
            {
                case WireKey:
                    // A valid wire field, so proto3 JSON would accept it silently; refused here
                    // so an authored document has one spelling.
                    issues.Add(new SourceIssue(SourceCodes.DocumentMalformed,
                        "'result' is not a source-format key; write 'return' — with a value, or bare to end the path with none.",
                        file, (int)key.Start.Line, (int)key.Start.Column, $"{path}.{WireKey}"));
                    rewritten.Add(key, value);
                    break;

                case SourceKey:
                    rewritten.Add(new YamlScalarNode(WireKey), IsNoValue(value) ? new YamlMappingNode() : value);
                    break;

                default:
                    rewritten.Add(key, value);
                    break;
            }
        }

        return rewritten;
    }

    /// <summary>
    /// <c>return:</c> with nothing after it, <c>return: ~</c>, <c>return: null</c> — every way YAML
    /// spells "no value". A quoted <c>"null"</c> is a string, and is left to fail as one.
    /// </summary>
    private static bool IsNoValue(YamlNode value) =>
        value is YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain } scalar
        && (string.IsNullOrEmpty(scalar.Value) || scalar.Value is "~" or "null" or "Null" or "NULL");
}
