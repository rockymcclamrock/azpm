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

    /// <summary>When this profile last authenticated (<c>azpm add</c> / <c>login</c> / <c>import</c>).</summary>
    public DateTimeOffset? LastLogin { get; set; }

    public BrowserMapping? Browser { get; set; }

    /// <summary>"user" (default, interactive) or "service-principal".</summary>
    public string? Kind { get; set; }
}

/// <summary>
/// A service principal's credential (<c>sp.json</c>, sibling of <c>meta.json</c>).
/// Plaintext for now — see docs/design/service-principals.md and issue #9.
/// </summary>
public sealed class ServicePrincipal
{
    public required string ClientId { get; set; }
    public required string TenantId { get; set; }

    /// <summary>"secret" or "certificate".</summary>
    public required string Auth { get; set; }

    public string? Secret { get; set; }
    public string? CertificatePath { get; set; }
}

/// <summary>Which browser + browser-profile <c>azpm portal</c> should launch for this profile.</summary>
public sealed class BrowserMapping
{
    /// <summary>edge | chrome | firefox | default</summary>
    public required string Kind { get; set; }

    /// <summary>Browser-profile name / directory (not used for <c>default</c>).</summary>
    public string? Profile { get; set; }
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
[JsonSerializable(typeof(ServicePrincipal))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class AzpmJson : JsonSerializerContext;
