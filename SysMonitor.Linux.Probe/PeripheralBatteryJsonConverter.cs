using System.Text.Json;
using System.Text.Json.Serialization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Probe;

/// <summary>
/// Writes a peripheral battery without its <see cref="PeripheralBattery.Id"/>.
///
/// The id is an opaque hash, but it is still a key for a device a person
/// carries, and this output is meant to be pasted into a report. The text
/// output has always left it out; the JSON output now matches, so the two
/// formats say the same thing about the same snapshot.
/// </summary>
internal sealed class PeripheralBatteryJsonConverter : JsonConverter<PeripheralBattery>
{
    public override PeripheralBattery Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException("The probe writes a snapshot; it never reads one.");

    public override void Write(
        Utf8JsonWriter writer,
        PeripheralBattery value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();

        writer.WritePropertyName(nameof(PeripheralBattery.Kind));
        JsonSerializer.Serialize(writer, value.Kind, options);

        if (value.Model is null)
        {
            writer.WriteNull(nameof(PeripheralBattery.Model));
        }
        else
        {
            writer.WriteString(nameof(PeripheralBattery.Model), value.Model);
        }

        if (value.Percent is int percent)
        {
            writer.WriteNumber(nameof(PeripheralBattery.Percent), percent);
        }
        else
        {
            writer.WriteNull(nameof(PeripheralBattery.Percent));
        }

        writer.WritePropertyName(nameof(PeripheralBattery.State));
        JsonSerializer.Serialize(writer, value.State, options);

        writer.WriteEndObject();
    }
}
