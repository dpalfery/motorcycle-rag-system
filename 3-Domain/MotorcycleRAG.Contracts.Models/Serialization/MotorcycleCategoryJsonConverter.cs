using System.Text.Json;
using System.Text.Json.Serialization;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Contracts.Models.Serialization;

/// <summary>
/// Serializes and deserializes <see cref="MotorcycleCategory"/> as its canonical
/// lowercase wire string ("dirt", "touring", "sport", "cruiser"). Deserialization
/// is case-insensitive and rejects invalid category values with a
/// <see cref="JsonException"/> so the four-value invariant is enforced at the
/// boundary.
/// </summary>
/// <remarks>
/// Applied per-property on contract DTOs (e.g. <c>IngestionJobConfiguration.Category</c>)
/// via <c>[JsonConverter(typeof(MotorcycleCategoryJsonConverter))]</c>. The converter
/// lives in <c>Contracts.Models</c> (the wire/contract project) so the Domain value
/// object remains free of serialization concerns.
/// </remarks>
public sealed class MotorcycleCategoryJsonConverter : JsonConverter<MotorcycleCategory>
{
    /// <inheritdoc />
    public override MotorcycleCategory Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var raw = reader.GetString();

        if (!MotorcycleCategory.TryParse(raw, out var category))
        {
            throw new JsonException(
                $"'{raw}' is not a valid MotorcycleCategory. Valid values: dirt, touring, sport, cruiser.");
        }

        return category;
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        MotorcycleCategory value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
