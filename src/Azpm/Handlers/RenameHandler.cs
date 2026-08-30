namespace Azpm.Handlers;

/// <summary><c>azpm rename &lt;old&gt; &lt;new&gt;</c> — rename a profile directory.</summary>
public sealed class RenameHandler(ProfileStore store, TextWriter output)
{
    public int Run(string oldName, string newName)
    {
        ProfileName.Validate(newName);

        if (!store.Exists(oldName))
            throw new AzpmException(ExitCode.ProfileNotFound, $"profile '{oldName}' not found");
        if (store.Exists(newName))
            throw new AzpmException(ExitCode.UsageError, $"profile '{newName}' already exists");

        Directory.Move(store.Home.ProfileDir(oldName), store.Home.ProfileDir(newName));
        store.UpdateMeta(newName, m => m.Name = newName);

        output.WriteLine($"Renamed '{oldName}' -> '{newName}'.");
        if (Environment.GetEnvironmentVariable(ProfileEnv.Profile) == oldName)
            output.WriteLine($"note: this shell still points at '{oldName}' — run 'azpm use {newName}'.");
        return ExitCode.Ok;
    }
}
