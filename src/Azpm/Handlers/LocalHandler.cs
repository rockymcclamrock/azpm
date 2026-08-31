namespace Azpm.Handlers;

/// <summary><c>azpm local [name]</c> — manage the directory's <c>.azpm</c> file.</summary>
public sealed class LocalHandler(ProfileStore store, TextWriter output, TextWriter error)
{
    private readonly LocalTrust _trust = new(store.Home);

    /// <summary>
    /// Used by the <c>azpm init --auto</c> hook: print the resolved profile name, or exit non-zero.
    /// <paramref name="trustAll"/> is set by <c>init --fullauto</c> and skips the trust gate.
    /// </summary>
    public int Resolve(bool trustAll = false)
    {
        var found = LocalFile.Find(Directory.GetCurrentDirectory());
        if (found is null)
            return ExitCode.UsageError;
        if (!ProfileName.IsValid(found.Profile))
        {
            error.WriteLine($"azpm: ignoring {found.FilePath} — '{found.Profile}' is not a valid profile name");
            return ExitCode.UsageError;
        }
        if (!trustAll && !_trust.IsTrusted(found.FilePath))
            return ExitCode.NotTrusted;
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

        var known = store.Exists(found.Profile);
        var trusted = _trust.IsTrusted(found.FilePath);
        var notes = string.Join(", ", new[]
        {
            known ? null : "profile missing!",
            trusted ? null : "not trusted for --auto — run 'azpm local allow'",
        }.Where(x => x is not null));

        output.WriteLine($"{found.Profile}  (from {found.FilePath}){(notes.Length > 0 ? $"  [{notes}]" : "")}");
        return known ? ExitCode.Ok : ExitCode.ProfileNotFound;
    }

    public int Set(string name)
    {
        _ = store.Load(name); // throws ProfileNotFound
        var path = LocalFile.Write(Directory.GetCurrentDirectory(), name);
        _trust.Allow(path); // you wrote it here, so it's trusted
        output.WriteLine($"wrote {path} -> {name}");
        return ExitCode.Ok;
    }

    public int Allow()
    {
        var found = LocalFile.Find(Directory.GetCurrentDirectory());
        if (found is null)
        {
            error.WriteLine($"azpm: no {LocalFile.Name} in this directory or a parent");
            return ExitCode.UsageError;
        }
        if (!ProfileName.IsValid(found.Profile))
        {
            error.WriteLine($"azpm: {found.FilePath} contains an invalid profile name ('{found.Profile}')");
            return ExitCode.UsageError;
        }
        _trust.Allow(found.FilePath);
        output.WriteLine($"trusted {found.FilePath} -> {found.Profile}");
        if (!store.Exists(found.Profile))
            error.WriteLine($"note: profile '{found.Profile}' doesn't exist yet — create it with 'azpm add {found.Profile}'");
        return ExitCode.Ok;
    }

    public int Unset()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), LocalFile.Name);
        if (File.Exists(path))
        {
            _trust.Forget(path);
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
