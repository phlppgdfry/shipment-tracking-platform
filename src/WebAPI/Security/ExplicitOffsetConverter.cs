using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace ShipmentTracking.WebAPI.Security;

/// <summary>The JavaScript integration uses millisecond precision and requires an explicit timezone.</summary>
public sealed partial class ExplicitOffsetConverter : JsonConverter<DateTimeOffset>
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?(?:Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex Format();
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var text=reader.TokenType==JsonTokenType.String?reader.GetString():null;
        if(text is null || !Format().IsMatch(text) || !DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.None,out var value))
            throw new JsonException("Use an ISO timestamp with seconds, explicit timezone and at most millisecond precision.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer,DateTimeOffset value,JsonSerializerOptions options)
        =>writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz",CultureInfo.InvariantCulture));
}
