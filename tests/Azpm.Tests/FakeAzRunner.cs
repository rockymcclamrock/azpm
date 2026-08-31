using Azpm;

namespace Azpm.Tests;

/// <summary>
/// Stand-in for <see cref="AzCli"/>. Records every call and, on <c>login</c>, can simulate a
/// successful sign-in by writing an <c>azureProfile.json</c> into the config dir.
/// </summary>
public sealed class FakeAzRunner : IAzRunner
{
    public List<(string ConfigDir, string[] Args)> Calls { get; } = [];

    public int ExitCode { get; set; }
    public AzResult CaptureResult { get; set; } = new(0, "", false);

    public AzResult Capture(string configDir, IReadOnlyList<string> args, TimeSpan timeout)
    {
        Calls.Add((configDir, [.. args]));
        return CaptureResult;
    }
    public bool SimulateLoginWritesProfile { get; set; } = true;
    public string LoginAccount { get; set; } = "user@contoso.example.com";
    public string LoginTenantDomain { get; set; } = "contoso.example.com";
    public string LoginSubscription { get; set; } = "Example Subscription";

    public int Run(string configDir, IReadOnlyList<string> args)
    {
        Calls.Add((configDir, [.. args]));

        if (args is ["logout", ..] && SimulateLoginWritesProfile)
        {
            var f = Path.Combine(configDir, "azureProfile.json");
            if (File.Exists(f)) File.Delete(f);
            return ExitCode;
        }

        if (ExitCode == 0 && args.Count > 0 && args[0] == "login" && SimulateLoginWritesProfile)
        {
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "azureProfile.json"), $$"""
            {
              "subscriptions": [
                {
                  "id": "00000000-0000-0000-0000-000000000000",
                  "name": "{{LoginSubscription}}",
                  "state": "Enabled",
                  "isDefault": true,
                  "tenantId": "11111111-1111-1111-1111-111111111111",
                  "tenantDefaultDomain": "{{LoginTenantDomain}}",
                  "user": { "name": "{{LoginAccount}}", "type": "user" }
                }
              ]
            }
            """);
        }

        return ExitCode;
    }
}
