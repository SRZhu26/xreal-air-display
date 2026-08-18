using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhoenixAirViewer.Core
{
    public sealed class QuaternionJsonConverter : JsonConverter<Quaternion>
    {
        public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("A quaternion must be a JSON object.");
            }

            float x = 0.0f;
            float y = 0.0f;
            float z = 0.0f;
            float w = 0.0f;
            bool hasX = false;
            bool hasY = false;
            bool hasZ = false;
            bool hasW = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("A quaternion property name was expected.");
                }

                string propertyName = reader.GetString();
                if (!reader.Read())
                {
                    throw new JsonException("A quaternion property value was expected.");
                }

                switch (propertyName)
                {
                    case "X":
                    case "x":
                        x = reader.GetSingle();
                        hasX = true;
                        break;
                    case "Y":
                    case "y":
                        y = reader.GetSingle();
                        hasY = true;
                        break;
                    case "Z":
                    case "z":
                        z = reader.GetSingle();
                        hasZ = true;
                        break;
                    case "W":
                    case "w":
                        w = reader.GetSingle();
                        hasW = true;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (!hasX || !hasY || !hasZ || !hasW)
            {
                throw new JsonException("A quaternion must contain X, Y, Z, and W.");
            }

            Quaternion value = new Quaternion(x, y, z, w);
            Quaternion normalized;
            if (!PoseMath.TryNormalize(value, out normalized))
            {
                throw new JsonException("A quaternion must be finite and non-zero.");
            }

            return normalized;
        }

        public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteNumber("Z", value.Z);
            writer.WriteNumber("W", value.W);
            writer.WriteEndObject();
        }
    }
}