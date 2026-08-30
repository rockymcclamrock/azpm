namespace Azpm.Handlers;

/// <summary><c>azpm add &lt;name&gt;</c> — create an isolated profile and run <c>az login</c> into it.</summary>
public sealed class AddHandler(ProfileStore store, IAzRunner az, TextWriter output)
{
    public int Run(string name, string? tenant, bool deviceCode, string? description)
    {
        store.Create(name, description, tenant);
        try
        {
            return new LoginHandler(store, az, output).Run(name, tenant, deviceCode, reset: false);
        }
        catch (AzpmException)
        {
            // A failed first login leaves nothing behind.
            store.Delete(name);
            throw;
        }
    }
}
