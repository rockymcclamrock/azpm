using System.Text.Json.Serialization;

namespace Azpm;

/// <summary>azpm's own per-profile metadata file (<c>meta.json</c>). Owned by azpm, never by az.</summary>
public sealed class ProfileMeta
{
    public required string Name { get; set; }
    public DateTimeOffset Created { get; set; }
    public string? TenantHint { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? LastUsed { get; set; }
}

/// <summary>The subset of az's <c>azureProfile.json</c> that azpm reads.</summary>
public sealed class AzureProfileFile
{
    [JsonPropertyName("subscriptions")]
    public List<AzureSubscription> Subscriptions { get; set; } = [];
}

public sealed class AzureSubscription
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
    [JsonPropertyName("tenantId")] public string? TenantId { get; set; }
    [JsonPropertyName("tenantDefaultDomain")] public string? TenantDefaultDomain { get; set; }
    [JsonPropertyName("user")] public AzureUser? User { get; set; }
}

public sealed class AzureUser
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProfileMeta))]
[JsonSerializable(typeof(AzureProfileFile))]
internal sealed partial class AzpmJson : JsonSerializerContext;
