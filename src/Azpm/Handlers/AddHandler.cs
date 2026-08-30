namespace Azpm.Handlers;

/// <summary><c>azpm add &lt;name&gt;</c> — create an isolated profile and log in.</summary>
public sealed class AddHandler(ProfileStore store, IAzRunner az, TextWriter output)
{
    public int Run(string name, InteractiveLogin interactive, ServicePrincipal? sp, string? description)
    {
        store.Create(name, description, interactive.Tenant ?? sp?.TenantId);
        try
        {
            return new LoginHandler(store, az, output).Run(name, interactive, sp, reset: false);
        }
        catch (AzpmException)
        {
            // A failed first login should leave nothing behind.
            try
            {
                store.Delete(name);
            }
            catch (AzpmException)
            {
                output.WriteLine($"note: profile '{name}' was left behind — remove it with 'azpm rm {name}'");
            }
            throw;
        }
    }
}
