using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azpm.Handlers;

/// <summary><c>azpm ls</c> — list every profile with its account, tenant, subscription and status.</summary>
public sealed class LsHandler(ProfileStore store, TextWriter output)
{
    public int Run(bool json)
    {
        var current = Environment.GetEnvironmentVariable("AZPM_PROFILE");
        var rows = store.LoadAll().Select(p =>
        {
            var sub = p.ActiveSubscription;
            var isSp = p.Meta?.Kind == "service-principal"
                || string.Equals(sub?.User?.Type, "servicePrincipal", StringComparison.OrdinalIgnoreCase);
            return new LsRow(
                p.Name,
                p.Name == current,
                sub?.User?.Name,
                sub?.TenantDefaultDomain ?? sub?.TenantId,
                sub?.Name,
                p.Status,
                isSp);
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

        var table = new TextTable("", "NAME", "ACCOUNT", "TENANT", "SUBSCRIPTION", "STATUS");
        foreach (var r in rows)
            table.AddRow(r.Current ? "*" : "", r.Name + (r.ServicePrincipal ? " (sp)" : ""),
                r.Account ?? "-", r.Tenant ?? "-", r.Subscription ?? "-", r.Status);
        table.RenderTo(output);
        return ExitCode.Ok;
    }
}

public sealed record LsRow(
    string Name,
    bool Current,
    string? Account,
    string? Tenant,
    string? Subscription,
    string Status,
    bool ServicePrincipal);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<LsRow>))]
internal sealed partial class LsJson : JsonSerializerContext;
