namespace Azpm;

/// <summary>A loaded profile: its directory plus whatever az and azpm have written into it.</summary>
public sealed class Profile
{
    public required string Name { get; init; }
    public required string ConfigDir { get; init; }
    public ProfileMeta? Meta { get; init; }
    public AzureProfileFile? AzureProfile { get; init; }

    public AzureSubscription? ActiveSubscription =>
        AzureProfile?.Subscriptions.FirstOrDefault(s => s.IsDefault)
        ?? AzureProfile?.Subscriptions.FirstOrDefault();

    /// <summary>Best-effort status (SPEC.md §7, decision S3): "ready" if a login is present.</summary>
    public string Status =>
        AzureProfile is { Subscriptions.Count: > 0 } ? "ready" : "logged out";
}
