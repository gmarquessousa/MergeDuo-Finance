using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace MergeDuo.Partnership.Infra.Cosmos;

public sealed class SystemTextJsonCosmosSerializer : CosmosSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (typeof(Stream).IsAssignableFrom(typeof(T)))
            {
                return (T)(object)stream;
            }

            return JsonSerializer.Deserialize<T>(stream, Options)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var output = new MemoryStream();
        JsonSerializer.Serialize(output, input, Options);
        output.Position = 0;
        return output;
    }
}
