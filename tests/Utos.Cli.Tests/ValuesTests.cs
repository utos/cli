using Utos.Cli.Core.Rendering;
using Utos.Workflows.V1;
using Xunit;

namespace Utos.Cli.Tests;

public class ValuesTests
{
    [Fact]
    public void Writes_a_value_without_blobs_as_plain_json()
    {
        var map = WorkflowValues.MapFromJson("""{"orderId": 42, "tags": ["a", "b"], "note": null}""");

        Assert.Equal("""{"orderId":42,"tags":["a","b"],"note":null}""", Values.Format(map, indented: false));
    }

    [Fact]
    public void Indents_on_request()
    {
        var map = WorkflowValues.MapFromJson("""{"ok": true}""");

        Assert.Equal("{\n  \"ok\": true\n}", Values.Format(map, indented: true).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Writes_a_value_holding_a_blob_in_the_protobuf_mapping()
    {
        // A blob has no plain-JSON form; the typed mapping is the one that cannot be mistaken for
        // data the workflow returned.
        var map = new WorkflowMap();
        map.Fields["photo"] = new WorkflowValue
        {
            BlobValue = new Blob { Id = "b-1", Size = 3, MediaType = "image/png" },
        };

        var text = Values.Format(map, indented: false);

        Assert.Contains("\"blobValue\"", text);
        Assert.Contains("\"b-1\"", text);
    }
}
