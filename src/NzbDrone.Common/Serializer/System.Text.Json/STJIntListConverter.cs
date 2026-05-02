using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Common.Serializer
{
    /// <summary>
    /// Deserialises <c>List&lt;int&gt;</c> while silently skipping any <c>null</c>
    /// elements.  The frontend can send <c>trackIds: [null]</c> when a track has
    /// not yet been matched to a database record; without this converter the
    /// default deserialiser throws because <c>null</c> cannot be assigned to
    /// <c>int</c>.
    /// </summary>
    public class STJIntListConverter : JsonConverter<List<int>>
    {
        public override List<int> Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException($"Expected '[', got {reader.TokenType}");
            }

            var list = new List<int>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return list;
                }

                if (reader.TokenType == JsonTokenType.Null)
                {
                    // Skip null elements — an unmatched track has no database ID.
                    continue;
                }

                list.Add(reader.GetInt32());
            }

            throw new JsonException("Unexpected end of JSON while reading array");
        }

        public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value)
            {
                writer.WriteNumberValue(item);
            }

            writer.WriteEndArray();
        }
    }
}
