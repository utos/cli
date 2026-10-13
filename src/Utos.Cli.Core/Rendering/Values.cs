using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Utos.Workflows.V1;

namespace Utos.Cli.Core.Rendering;

/// <summary>
/// A run's values as text: plain JSON, as the author wrote and the workflow returned them.
/// <para>
/// A value holding a blob has no plain-JSON form, so it is written in the protobuf JSON mapping
/// instead — unambiguous, and the one form the SDK names for it. Inventing a shorter spelling here
/// would print something that looks like data and isn't.
/// </para>
/// </summary>
public static class Values
{
    public static string Format(WorkflowMap map, bool indented)
    {
        if (!WorkflowValues.TryToJson(new WorkflowValue { MapValue = map }, out var json))
            return (indented ? Indented : Compact).Format(map);

        return indented ? Indent(json) : json;
    }

    private static string Indent(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            document.WriteTo(writer);

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static readonly JsonFormatter Compact = JsonFormatter.Default;
    private static readonly JsonFormatter Indented = new(JsonFormatter.Settings.Default.WithIndentation());
}
