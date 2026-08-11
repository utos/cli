using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Utos.Cli.Core.Source;

/// <summary>
/// Converts a YAML node graph into <see cref="JsonNode"/>, so the result can be handed to
/// protobuf's proto3-JSON parser.
/// <para>
/// Only YamlDotNet's representation model is used — never its reflection-based object
/// deserializer, which would not survive trimming in a NativeAOT binary.
/// </para>
/// </summary>
internal static partial class YamlJson
{
    /// <summary>
    /// Loads a YAML document, rejecting duplicate mapping keys.
    /// <para>
    /// YAML 1.2 says duplicate keys are invalid, but parsers commonly take last-wins silently —
    /// which would halve a workflow's activities without a word.
    /// </para>
    /// </summary>
    public static YamlMappingNode LoadDocument(string text, string file)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            // YamlDotNet rejects duplicate keys while building the mapping, so that case arrives
            // here rather than reaching the explicit sweep below. Give it its own code either way.
            var duplicate = ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                            || ex.Message.Contains("same key", StringComparison.OrdinalIgnoreCase);

            throw new WorkflowSourceException(new SourceIssue(
                duplicate ? SourceCodes.DuplicateKey : SourceCodes.DocumentMalformed,
                ex.Message.Trim(), file, (int)ex.Start.Line, (int)ex.Start.Column));
        }

        if (stream.Documents.Count == 0)
        {
            throw new WorkflowSourceException(new SourceIssue(
                SourceCodes.DocumentMalformed, "File is empty.", file));
        }

        if (stream.Documents.Count > 1)
        {
            var second = stream.Documents[1].RootNode;
            throw new WorkflowSourceException(new SourceIssue(
                SourceCodes.DocumentMalformed,
                "A workflow file must contain exactly one document.", file,
                (int)second.Start.Line, (int)second.Start.Column));
        }

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new WorkflowSourceException(new SourceIssue(
                SourceCodes.DocumentMalformed, "The document root must be a mapping.", file,
                (int)stream.Documents[0].RootNode.Start.Line, (int)stream.Documents[0].RootNode.Start.Column));
        }

        RejectDuplicateKeys(root, file);
        return root;
    }

    /// <summary>
    /// Converts a YAML node to JSON, resolving plain scalars with the YAML 1.2 core schema.
    /// <para>
    /// Type inference is load-bearing: <c>detached: true</c> must reach protobuf as a JSON boolean
    /// and <c>requiredCount: 2</c> as a number, while <c>duration: 30s</c> must stay a string.
    /// Quoted scalars are always strings, which is how an author forces the issue.
    /// </para>
    /// </summary>
    public static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode mapping => ToJsonObject(mapping),
        YamlSequenceNode sequence => ToJsonArray(sequence),
        YamlScalarNode scalar => ToJsonScalar(scalar),
        _ => null,
    };

    private static JsonObject ToJsonObject(YamlMappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var (key, value) in mapping.Children)
            result[Key(key)] = ToJson(value);

        return result;
    }

    private static JsonArray ToJsonArray(YamlSequenceNode sequence)
    {
        var result = new JsonArray();
        foreach (var item in sequence.Children)
            result.Add(ToJson(item));

        return result;
    }

    private static JsonNode? ToJsonScalar(YamlScalarNode scalar)
    {
        // A quoted or block scalar is a string by construction; only plain scalars are resolved.
        if (scalar.Style != ScalarStyle.Plain) return JsonValue.Create(scalar.Value ?? string.Empty);

        var text = scalar.Value;

        if (string.IsNullOrEmpty(text) || text is "~" or "null" or "Null" or "NULL") return null;

        if (text is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (text is "false" or "False" or "FALSE") return JsonValue.Create(false);

        if (IntegerPattern().IsMatch(text)
            && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (FloatPattern().IsMatch(text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(text);
    }

    /// <summary>The key of a mapping entry, which the spec requires to be a scalar.</summary>
    public static string Key(YamlNode node) =>
        node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : node.ToString();

    private static void RejectDuplicateKeys(YamlNode node, string file)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (key, value) in mapping.Children)
                {
                    var name = Key(key);
                    if (!seen.Add(name))
                    {
                        throw new WorkflowSourceException(new SourceIssue(
                            SourceCodes.DuplicateKey, $"Duplicate key '{name}'.", file,
                            (int)key.Start.Line, (int)key.Start.Column));
                    }

                    RejectDuplicateKeys(value, file);
                }

                break;
            }
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children) RejectDuplicateKeys(item, file);
                break;
        }
    }

    [GeneratedRegex(@"^[-+]?[0-9]+$")]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    private static partial Regex FloatPattern();
}
