namespace Azpm.Handlers;

/// <summary><c>azpm path &lt;name&gt;</c> — print a profile's <c>AZURE_CONFIG_DIR</c>.</summary>
public sealed class PathHandler(ProfileStore store, TextWriter output)
{
    public int Run(string name)
    {
        output.WriteLine(store.Load(name).ConfigDir);
        return ExitCode.Ok;
    }
}
