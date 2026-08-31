using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azpm.Handlers;

/// <summary><c>azpm ls</c> — list every profile with its account, tenant, subscription and status.</summary>
public sealed class LsHandler(ProfileStore store, TextWriter output, Func<IAzRunner>? azFactory = null)
{
    public int Run(bool json, bool check = false)
    {
        var current = Environment.GetEnvironmentVariable("AZPM_PROFILE");
        var az = check ? (azFactory ?? throw new AzpmException(ExitCode.AzNotFound, "--check needs 'az'"))() : null;

        var rows = store.LoadAll().Select(p =>
        {
            var sub = p.ActiveSubscription;
            var isSp = p.Meta?.Kind == "service-principal"
                || string.Equals(sub?.User?.Type, "servicePrincipal", StringComparison.OrdinalIgnoreCase);

            var status = p.Status;
            if (az is not null && status == "ready")
            {
                var r = az.Capture(p.ConfigDir, ["account", "get-access-token", "--output", "none"],
                    TimeSpan.FromSeconds(20));
                status = r.TimedOut ? "check timed out" : r.ExitCode == 0 ? "valid" : "needs login";
            }

            return new LsRow(
                p.Name,
                p.Name == current,
                sub?.User?.Name,
                sub?.TenantDefaultDomain ?? sub?.TenantId,
                sub?.Name,
                status,
                isSp,
                p.Meta?.LastLogin);
        }).ToList();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(rows, LsJson.Default.ListLsRow));
            return ExitCode.Ok;
        }

        if (rows.Count == 0)
        {
            output.WriteLine("No profiles yet. Create one with 'azpm add <name>'.");
            return ExitCode.Ok;
        }

        var table = new TextTable("", "NAME", "ACCOUNT", "TENANT", "SUBSCRIPTION", "STATUS", "LOGIN");
        foreach (var r in rows)
            table.AddRow(r.Current ? "*" : "", r.Name + (r.ServicePrincipal ? " (sp)" : ""),
                r.Account ?? "-", r.Tenant ?? "-", r.Subscription ?? "-", r.Status, Ago(r.LastLogin));
        table.RenderTo(output);
        return ExitCode.Ok;
    }

    /// <summary>"3d ago" / "5h ago" / "12m ago" / "just now" / "-".</summary>
    private static string Ago(DateTimeOffset? when)
    {
        if (when is null)
            return "-";

        var d = DateTimeOffset.UtcNow - when.Value;
        if (d < TimeSpan.Zero)
            d = TimeSpan.Zero;

        return d.TotalMinutes < 1 ? "just now"
            : d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes}m ago"
            : d.TotalHours < 24 ? $"{(int)d.TotalHours}h ago"
            : $"{(int)d.TotalDays}d ago";
    }
}

public sealed record LsRow(
    string Name,
    bool Current,
    string? Account,
    string? Tenant,
    string? Subscription,
    string Status,
    bool ServicePrincipal,
    DateTimeOffset? LastLogin);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<LsRow>))]
internal sealed partial class LsJson : JsonSerializerContext;
