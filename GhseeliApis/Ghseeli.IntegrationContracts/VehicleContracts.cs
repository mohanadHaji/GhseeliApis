using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghseeli.IntegrationContracts.Vehicles;

[JsonConverter(typeof(VehicleTypeJsonConverter))]
public enum VehicleType
{
    Sedan = 0,
    Motorcycle = 1,
    Suv5Seater = 2,
    Suv7Seater = 3,
    Van7Seater = 4
}

public sealed class VehicleTypeJsonConverter : JsonConverter<VehicleType>
{
    public override VehicleType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.TokenType == JsonTokenType.String
            ? reader.GetString() switch
            {
                nameof(VehicleType.Sedan) => VehicleType.Sedan,
                nameof(VehicleType.Motorcycle) => VehicleType.Motorcycle,
                nameof(VehicleType.Suv5Seater) => VehicleType.Suv5Seater,
                nameof(VehicleType.Suv7Seater) => VehicleType.Suv7Seater,
                nameof(VehicleType.Van7Seater) => VehicleType.Van7Seater,
                _ => (VehicleType?)null
            }
            : null;
        if (value.HasValue)
        {
            return value.Value;
        }

        throw new JsonException("The vehicle type is invalid.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        VehicleType value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
