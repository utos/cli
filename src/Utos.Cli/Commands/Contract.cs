using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Utos.Workflows.V1;

namespace Utos.Cli.Commands;

/// <summary>
/// Renders what a workflow declares about the data crossing its boundaries — what
/// <c>api/docs/workflow-schemas.md</c> calls its contract, and what a registry shows on a
/// workflow's page.
/// <para>
/// A schema in a bundle is plain JSON Schema 2020-12; printing it raw would be accurate and
/// unreadable. This shows the shape an author cares about — which properties, which are required
/// — and leaves the rest to <c>--json</c> or to reading the document.
/// </para>
/// </summary>
internal static class Contract
{
    /// <summary>
    /// Writes the entry workflow's contract, or nothing at all when it declares none. Silence is
    /// right there: no declaration is the empty schema, and a row reading "none" would suggest a
    /// document had said something it did not.
    /// </summary>
    internal static void Write(WorkflowBundle bundle)
    {
        if (!bundle.Workflows.TryGetValue(bundle.EntryPoint, out var entry)) return;

        var spec = entry.Spec;
        if (spec is null) return;

        // Every activity that declares an input, not just one. A run is scheduled with a start
        // activity and its input goes to *that* activity, so a document with three entry points
        // has three input shapes — which is the whole reason input is declared per activity.
        var accepts = spec.Activities
            .Where(a => a.Value?.Schema?.Input is not null)
            .OrderBy(a => a.Key, System.StringComparer.Ordinal)
            .ToList();

        if (spec.Env is null && spec.Output is null && spec.Emits is null && accepts.Count == 0) return;

        Output.Line();
        Output.Line(Output.Bold("Contract"));

        if (spec.Env is not null) Row("env", Describe(spec.Env));
        if (spec.Output is not null) Row("output", Describe(spec.Output));
        if (spec.Emits is not null) Row("emits", Describe(spec.Emits));

        if (accepts.Count == 0) return;

        Output.Line($"  {Output.Dim("accepts")}");
        foreach (var (name, activity) in accepts)
            Output.Line($"    {name,-14}{Describe(activity.Schema.Input)}");
    }

    private static void Row(string label, string value) =>
        Output.Line($"  {Output.Dim(label.PadRight(10))}{value}");

    /// <summary>
    /// A schema's properties, with required ones marked. Ordered as the schema lists them rather
    /// than alphabetically, since a compiled <c>properties</c> map keeps the author's order.
    /// </summary>
    private static string Describe(Struct schema)
    {
        var required = new HashSet<string>(System.StringComparer.Ordinal);
        if (schema.Fields.TryGetValue("required", out var declared)
            && declared.KindCase == Value.KindOneofCase.ListValue)
        {
            foreach (var entry in declared.ListValue.Values)
            {
                if (entry.KindCase == Value.KindOneofCase.StringValue) required.Add(entry.StringValue);
            }
        }

        if (!schema.Fields.TryGetValue("properties", out var properties)
            || properties.KindCase != Value.KindOneofCase.StructValue
            || properties.StructValue.Fields.Count == 0)
        {
            // A declared object with no properties is not nothing: closed, it accepts only {}.
            return Output.Dim("(no properties)");
        }

        var parts = properties.StructValue.Fields.Select(property =>
        {
            var type = TypeOf(property.Value);
            var name = required.Contains(property.Key) ? property.Key : property.Key + "?";
            return type is null ? name : $"{name}: {type}";
        });

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The declared type, rendered back in the short form's spelling — a union with null reads as
    /// the <c>nullable</c> the author wrote, rather than as the array it compiled to.
    /// </summary>
    private static string? TypeOf(Value property)
    {
        if (property.KindCase != Value.KindOneofCase.StructValue) return null;
        if (!property.StructValue.Fields.TryGetValue("type", out var type)) return null;

        return type.KindCase switch
        {
            Value.KindOneofCase.StringValue => type.StringValue,

            Value.KindOneofCase.ListValue => string.Join(" | ", type.ListValue.Values
                .Where(v => v.KindCase == Value.KindOneofCase.StringValue)
                .Select(v => v.StringValue)),

            _ => null
        };
    }
}
