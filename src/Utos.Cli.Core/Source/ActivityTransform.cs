using Google.Protobuf.Reflection;
using Utos.Workflows.V1;
using YamlDotNet.RepresentationModel;

namespace Utos.Cli.Core.Source;

/// <summary>
/// Rewrites authored activities into the shape protobuf expects.
/// <para>
/// Source form puts the activity kind in a <c>type</c> key with its configuration flat alongside;
/// the proto models it as a <c>oneof</c>, i.e. nested under the kind's field name. Where the
/// selected configuration declares a oneof of its own, <c>type</c> continues as a dot-separated
/// path — <c>workflow.call</c>, <c>promise.count</c> — and the configuration nests one level
/// deeper. The mapping is specified in <c>api/docs/workflow-source-format.md</c>.
/// </para>
/// <para>
/// Everything here is derived from <see cref="WorkflowActivity.Descriptor"/> rather than a
/// hard-coded table, so a new activity kind or mode added to the spec becomes authorable the
/// moment the SDK package is bumped — no code change here.
/// </para>
/// </summary>
internal static class ActivityTransform
{
    /// <summary>
    /// A resolved <c>type</c> path: the oneof field names to nest under, and the messages those
    /// segments land on. <c>Messages[i]</c> is the message selected by <c>Segments[i]</c>.
    /// </summary>
    /// <param name="Segments">Oneof field names, outermost first, in canonical spelling.</param>
    /// <param name="Placement">
    /// Accepted key spelling to the index in <paramref name="Segments"/> whose message declares it.
    /// Unambiguous because no field name is declared at two levels of one path — an invariant of
    /// the protos, asserted by the SDK's descriptor tests.
    /// </param>
    private sealed record ConfigKind(
        IReadOnlyList<string> Segments,
        IReadOnlyDictionary<string, int> Placement);

    /// <summary>
    /// The legal <c>type</c> values, keyed by every spelling proto3 JSON accepts for each segment.
    /// <para>
    /// Synthetic oneofs are excluded. proto3 <c>optional</c> generates one oneof per field, and
    /// those are not activity kinds.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ConfigKind> ConfigKinds = BuildConfigKinds();

    /// <summary>
    /// Field names that stay at activity level rather than moving into the configuration —
    /// everything on <see cref="WorkflowActivity"/> outside the oneof, in both spellings proto3
    /// JSON accepts.
    /// </summary>
    private static readonly IReadOnlySet<string> ActivityLevelKeys = BuildActivityLevelKeys();

    /// <summary>The key carrying the activity kind.</summary>
    public const string TypeKey = "type";

    /// <summary>Legal <c>type</c> values in canonical spelling, sorted, for error messages.</summary>
    public static IReadOnlyList<string> KnownTypes { get; } = ConfigKinds.Values
        .Select(k => string.Join('.', k.Segments))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Returns a copy of <paramref name="activity"/> with its configuration nested under the
    /// field path named by <c>type</c>. Issues are accumulated rather than thrown so one run
    /// reports every bad activity.
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
            // Covers an unknown kind and a path that stops short of a mode the kind requires
            // (bare 'workflow'); both are UTOS-S007, and listing the legal paths names the fix.
            issues.Add(new SourceIssue(SourceCodes.ActivityTypeInvalid,
                $"Activity '{activityName}' has unknown type '{typeName}'. Expected one of: "
                + string.Join(", ", KnownTypes) + ".",
                file, (int)typeNode.Start.Line, (int)typeNode.Start.Column));
            return activity;
        }

        var rewritten = new YamlMappingNode();
        var buckets = new YamlMappingNode[kind.Segments.Count];
        for (var i = 0; i < buckets.Length; i++) buckets[i] = new YamlMappingNode();

        foreach (var (key, value) in activity.Children)
        {
            var name = YamlJson.Key(key);
            if (name == TypeKey) continue;

            if (ActivityLevelKeys.Contains(name))
            {
                rewritten.Add(key, value);
            }
            else if (kind.Placement.TryGetValue(name, out var level))
            {
                buckets[level].Add(key, value);
            }
            else
            {
                // Belongs to no message on the path. Left on the innermost one deliberately:
                // proto3-JSON parsing rejects it as an unknown field, which is a better error than
                // anything invented at this layer.
                buckets[^1].Add(key, value);
            }
        }

        // Nest inwards-out: the innermost bucket becomes a key of the one enclosing it.
        for (var i = kind.Segments.Count - 1; i >= 1; i--)
        {
            buckets[i - 1].Add(new YamlScalarNode(kind.Segments[i]), buckets[i]);
        }

        rewritten.Add(new YamlScalarNode(kind.Segments[0]), buckets[0]);
        return rewritten;
    }

    private static Dictionary<string, ConfigKind> BuildConfigKinds()
    {
        var kinds = new Dictionary<string, ConfigKind>(StringComparer.Ordinal);
        Walk(WorkflowActivity.Descriptor, [], kinds);
        return kinds;

        // `path` is the oneof fields traversed so far, outermost first. Carrying the fields
        // themselves rather than their message types keeps the mapping exact even if two modes
        // ever share a configuration message.
        static void Walk(MessageDescriptor message, List<FieldDescriptor> path,
            Dictionary<string, ConfigKind> into)
        {
            var oneofs = message.Oneofs.Where(o => !o.IsSynthetic).ToList();

            if (oneofs.Count == 0)
            {
                // A complete path: the message it reached declares no further choice.
                if (path.Count > 0) Register(path, into);
                return;
            }

            foreach (var oneof in oneofs)
            {
                foreach (var field in oneof.Fields)
                {
                    path.Add(field);
                    Walk(field.MessageType, path, into);
                    path.RemoveAt(path.Count - 1);
                }
            }
        }

        // Registers a path under every spelling proto3 JSON accepts for each of its segments, so
        // `start_activity` and `startActivity` — and `on_emitted` and `onEmitted` — are equally
        // authorable, matching the parser requirement in the source-format spec.
        static void Register(List<FieldDescriptor> path, Dictionary<string, ConfigKind> into)
        {
            var placement = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var level = 0; level < path.Count; level++)
            {
                foreach (var field in path[level].MessageType.Fields.InDeclarationOrder())
                {
                    if (field.ContainingOneof is { IsSynthetic: false }) continue;

                    placement[field.Name] = level;
                    placement[field.JsonName] = level;
                }
            }

            var kind = new ConfigKind(path.Select(f => f.JsonName).ToArray(), placement);

            foreach (var spelling in Spellings(path, 0))
            {
                into[spelling] = kind;
            }
        }

        static IEnumerable<string> Spellings(List<FieldDescriptor> path, int index)
        {
            var field = path[index];
            string[] names = field.Name == field.JsonName
                ? [field.JsonName]
                : [field.Name, field.JsonName];

            if (index == path.Count - 1)
            {
                foreach (var name in names) yield return name;
                yield break;
            }

            // Materialize the tail: the recursive enumerable is re-enumerated per name.
            var tails = Spellings(path, index + 1).ToList();

            foreach (var name in names)
            {
                foreach (var tail in tails) yield return name + "." + tail;
            }
        }
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
