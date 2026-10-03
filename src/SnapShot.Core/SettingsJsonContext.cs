using System.Text.Json.Serialization;

namespace SnapShot.Core;

/// <summary>Source-generated serializer for <see cref="Settings"/>, so no reflection is needed under Native AOT.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(Settings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
