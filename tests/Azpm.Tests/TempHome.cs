using Azpm;

namespace Azpm.Tests;

/// <summary>A throwaway <see cref="AzpmHome"/> in a temp directory, deleted on dispose.</summary>
public sealed class TempHome : IDisposable
{
    public AzpmHome Home { get; }
    public ProfileStore Store { get; }

    public TempHome()
    {
        var root = Path.Combine(Path.GetTempPath(), "azpm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Home = new AzpmHome(root);
        Store = new ProfileStore(Home);
    }

    /// <summary>Writes a fake az <c>azureProfile.json</c> into a profile's config dir.</summary>
    public void WriteAzureProfile(string profile, string account, string tenantDomain, string subscription)
    {
        var dir = Home.ConfigDir(profile);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "azureProfile.json"), $$"""
        {
          "subscriptions": [
            {
              "id": "00000000-0000-0000-0000-000000000000",
              "name": "{{subscription}}",
              "state": "Enabled",
              "isDefault": true,
              "tenantId": "11111111-1111-1111-1111-111111111111",
              "tenantDefaultDomain": "{{tenantDomain}}",
              "user": { "name": "{{account}}", "type": "user" }
            }
          ]
        }
        """);
    }

    public void Dispose()
    {
        try { Directory.Delete(Home.Root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}
