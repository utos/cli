using System.Text.Json.Nodes;
using Google.Protobuf;
using Utos.Workflows.V1;
using YamlDotNet.RepresentationModel;

namespace Utos.Cli.Core.Source;

/// <summary>Reads one authored workflow file into its protobuf form.</summary>
public static class WorkflowLoader
{
    /// <summary>Loads and parses <paramref name="path"/>.</summary>
    /// <exception cref="WorkflowSourceException">The file cannot be read or does not parse.</exception>
    public static Workflow LoadFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkflowSourceException(new SourceIssue(
                SourceCodes.DependencyFileUnreadable, ex.Message, path));
        }

        return Parse(text, path);
    }

    /// <summary>Parses authored YAML into a <see cref="Workflow"/>.</summary>
    /// <exception cref="WorkflowSourceException">The document does not parse.</exception>
    public static Workflow Parse(string text, string file)
    {
        var root = YamlJson.LoadDocument(text, file);

        var issues = new List<SourceIssue>();
        var transformed = RewriteActivities(root, file, issues);
        if (issues.Count > 0) throw new WorkflowSourceException(issues);

        var json = YamlJson.ToJson(transformed) as JsonObject
                   ?? throw new WorkflowSourceException(new SourceIssue(
                       SourceCodes.DocumentMalformed, "The document root must be a mapping.", file));

        try
        {
            // Unknown fields are rejected, which is what turns this parse into a schema check:
            // a misspelled key is a mistake, not a comment.
            return JsonParser.Default.Parse<Workflow>(json.ToJsonString());
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException or InvalidJsonException)
        {
            throw new WorkflowSourceException(new SourceIssue(
                SourceCodes.DocumentMalformed, Tidy(ex.Message), file));
        }
    }

    /// <summary>
    /// Applies <see cref="ActivityTransform"/> to every entry of <c>spec.activities</c>, leaving
    /// the rest of the tree alone — outside activities the source shape already matches the proto.
    /// </summary>
    private static YamlMappingNode RewriteActivities(YamlMappingNode root, string file,
        List<SourceIssue> issues)
    {
        if (Child(root, "spec") is not YamlMappingNode spec) return root;
        if (Child(spec, "activities") is not YamlMappingNode activities) return root;

        var rewrittenActivities = new YamlMappingNode();
        foreach (var (key, value) in activities.Children)
        {
            var name = YamlJson.Key(key);
            if (value is YamlMappingNode activity)
            {
                rewrittenActivities.Add(key, ActivityTransform.Rewrite(activity, name, file, issues));
            }
            else
            {
                issues.Add(new SourceIssue(SourceCodes.DocumentMalformed,
                    $"Activity '{name}' must be a mapping.", file, (int)value.Start.Line, (int)value.Start.Column));
            }
        }

        return Replace(root, "spec", Replace(spec, "activities", rewrittenActivities));
    }

    private static YamlNode? Child(YamlMappingNode mapping, string key)
    {
        foreach (var (k, v) in mapping.Children)
            if (YamlJson.Key(k) == key)
                return v;

        return null;
    }

    private static YamlMappingNode Replace(YamlMappingNode mapping, string key, YamlNode replacement)
    {
        var result = new YamlMappingNode();
        foreach (var (k, v) in mapping.Children)
            result.Add(k, YamlJson.Key(k) == key ? replacement : v);

        return result;
    }

    /// <summary>
    /// protobuf reports unknown fields as a bare exception message; prefix it so the reader knows
    /// which layer rejected the document.
    /// </summary>
    private static string Tidy(string message) =>
        message.StartsWith("Unknown field", StringComparison.Ordinal)
            ? message + " (fields are checked against the workflow schema)"
            : message;
}
