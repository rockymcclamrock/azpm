namespace Azpm.Handlers;

/// <summary><c>azpm local [name]</c> — manage the directory's <c>.azpm</c> file.</summary>
public sealed class LocalHandler(ProfileStore store, TextWriter output, TextWriter error)
{
    /// <summary>Used by the <c>azpm init --auto</c> hook: print the resolved profile name, or exit non-zero.</summary>
    public int Resolve()
    {
        var found = LocalFile.Find(Directory.GetCurrentDirectory());
        if (found is null)
            return ExitCode.UsageError;
        if (!ProfileName.IsValid(found.Profile))
        {
            error.WriteLine($"azpm: ignoring {found.FilePath} — '{found.Profile}' is not a valid profile name");
            return ExitCode.UsageError;
        }
        if (!store.Exists(found.Profile))
        {
            error.WriteLine($"azpm: {found.FilePath} names unknown profile '{found.Profile}'");
            return ExitCode.ProfileNotFound;
        }
        output.WriteLine(found.Profile);
        return ExitCode.Ok;
    }

    public int Show()
    {
        var found = LocalFile.Find(Directory.GetCurrentDirectory());
        if (found is null)
        {
            output.WriteLine($"no {LocalFile.Name} for this directory");
            return ExitCode.Ok;
        }
        if (!ProfileName.IsValid(found.Profile))
        {
            output.WriteLine($"{found.FilePath} contains an invalid profile name ('{found.Profile}') — ignored");
            return ExitCode.UsageError;
        }
        var ok = store.Exists(found.Profile);
        output.WriteLine($"{found.Profile}  (from {found.FilePath}){(ok ? "" : "  [profile missing!]")}");
        return ok ? ExitCode.Ok : ExitCode.ProfileNotFound;
    }

    public int Set(string name)
    {
        _ = store.Load(name); // throws ProfileNotFound
        var path = LocalFile.Write(Directory.GetCurrentDirectory(), name);
        output.WriteLine($"wrote {path} -> {name}");
        return ExitCode.Ok;
    }

    public int Unset()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), LocalFile.Name);
        if (File.Exists(path))
        {
            File.Delete(path);
            output.WriteLine($"removed {path}");
        }
        else
        {
            output.WriteLine($"no {LocalFile.Name} in this directory");
        }
        return ExitCode.Ok;
    }
}
