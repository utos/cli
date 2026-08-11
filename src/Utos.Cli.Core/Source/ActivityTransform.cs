using Google.Protobuf.Reflection;
using Utos.Workflows.V1;
using YamlDotNet.RepresentationModel;

namespace Utos.Cli.Core.Source;

/// <summary>
/// Rewrites authored activities into the shape protobuf expects.
/// <para>
/// Source form puts the activity kind in a <c>type</c> key with its configuration flat alongside;
/// the proto models it as a <c>oneof</c>, i.e. nested under the kind's field name. The mapping is
/// specified in <c>api/docs/workflow-source-format.md</c>.
/// </para>
/// <para>
/// Everything here is derived from <see cref="WorkflowActivity.Descriptor"/> rather than a
/// hard-coded table, so a new activity kind added to the spec becomes authorable the moment the
/// SDK package is bumped — no code change here.
/// </para>
/// </summary>
internal static class ActivityTransform
{
    /// <summary>
    /// The legal <c>type</c> values: the members of <see cref="WorkflowActivity"/>'s
    /// <c>config</c> oneof.
    /// <para>
    /// Synthetic oneofs are excluded. proto3 <c>optional</c> generates one oneof per field, and
    /// those are not activity kinds.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, FieldDescriptor> ConfigKinds = BuildConfigKinds();

    /// <summary>
    /// Field names that stay at activity level rather than moving into the configuration —
    /// everything on <see cref="WorkflowActivity"/> outside the oneof, in both spellings proto3
    /// JSON accepts.
    /// </summary>
    private static readonly IReadOnlySet<string> ActivityLevelKeys = BuildActivityLevelKeys();

    /// <summary>The key carrying the activity kind.</summary>
    public const string TypeKey = "type";

    /// <summary>Legal <c>type</c> values, sorted, for error messages.</summary>
    public static IReadOnlyList<string> KnownTypes { get; } = ConfigKinds.Values
        .Select(f => f.JsonName)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Returns a copy of <paramref name="activity"/> with its configuration nested under the field
    /// named by <c>type</c>. Issues are accumulated rather than thrown so one run reports every
    /// bad activity.
    /// </summary>
    public static YamlMappingNode Rewrite(YamlMappingNode activity, string activityName, string file,
        List<SourceIssue> issues)
    {
        YamlNode? typeNode = null;
        foreach (var (key, value) in activity.Children)
        {
            if (YamlJson.Key(key) == TypeKey) typeNode = value;
        }

        if (typeNode is not YamlScalarNode { Value: { Length: > 0 } typeName })
        {
            issues.Add(new SourceIssue(SourceCodes.ActivityTypeInvalid,
                $"Activity '{activityName}' has no 'type'. Expected one of: {string.Join(", ", KnownTypes)}.",
                file, (int)activity.Start.Line, (int)activity.Start.Column));
            return activity;
        }

        if (!ConfigKinds.TryGetValue(typeName, out var kind))
        {
            issues.Add(new SourceIssue(SourceCodes.ActivityTypeInvalid,
                $"Activity '{activityName}' has unknown type '{typeName}'. Expected one of: "
                + string.Join(", ", KnownTypes) + ".",
                file, (int)typeNode.Start.Line, (int)typeNode.Start.Column));
            return activity;
        }

        var rewritten = new YamlMappingNode();
        var config = new YamlMappingNode();

        foreach (var (key, value) in activity.Children)
        {
            var name = YamlJson.Key(key);
            if (name == TypeKey) continue;

            // Anything that is not an activity-level field belongs to the configuration. Keys that
            // belong to neither are left here deliberately: proto3-JSON parsing rejects them as
            // unknown fields, which is a better error than anything invented at this layer.
            if (ActivityLevelKeys.Contains(name)) rewritten.Add(key, value);
            else config.Add(key, value);
        }

        rewritten.Add(new YamlScalarNode(kind.JsonName), config);
        return rewritten;
    }

    private static Dictionary<string, FieldDescriptor> BuildConfigKinds()
    {
        var kinds = new Dictionary<string, FieldDescriptor>(StringComparer.Ordinal);

        foreach (var oneof in WorkflowActivity.Descriptor.Oneofs)
        {
            if (oneof.IsSynthetic) continue;

            foreach (var field in oneof.Fields)
            {
                kinds[field.Name] = field;
                kinds[field.JsonName] = field;
            }
        }

        return kinds;
    }

    private static HashSet<string> BuildActivityLevelKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in WorkflowActivity.Descriptor.Fields.InDeclarationOrder())
        {
            if (field.ContainingOneof is { IsSynthetic: false }) continue;

            keys.Add(field.Name);
            keys.Add(field.JsonName);
        }

        return keys;
    }
}
