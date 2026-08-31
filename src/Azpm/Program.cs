using System.CommandLine;
using Azpm;
using Azpm.Handlers;

var homeOption = new Option<string?>("--home")
{
    Description = "Override the azpm home directory (default: AZPM_HOME or ~/.azpm)",
    Recursive = true,
};

var root = new RootCommand("""
    azpm - Azure Profile Manager: isolated Azure CLI login profiles.

    Each profile is its own AZURE_CONFIG_DIR, so 'az' logins for different tenants or
    accounts sit side by side instead of overwriting each other.

    Everyday use:
      azpm add work                    create a profile and sign in
      azpm shell work                  subshell that IS 'work'  (type 'exit' to leave)
      azpm exec work -- az group list  run one command as 'work'
      azpm ls                          list profiles: account, tenant, sub, status, login age
      azpm portal work                 open the Portal as 'work' (after a one-time --browser bind)

    Switch this shell in place (no subshell), like nvm/direnv - needs a one-time setup line
    from 'azpm init' in your shell profile:
      azpm use work  /  azpm use dev  /  azpm deactivate

    'azpm' with no arguments picks a profile and drops you into its shell.
    Full guide: https://github.com/rockymcclamrock/azpm
    """);
root.Options.Add(homeOption);

ProfileStore Store(ParseResult r) => new(AzpmHome.Resolve(r.GetValue(homeOption)));

// Turn a thrown AzpmException into a clean "azpm: <msg>" + exit code. System.CommandLine's
// own Invoke() otherwise catches it and dumps a stack trace.
int Guard(Func<int> body)
{
    try
    {
        return body();
    }
    catch (AzpmException ex)
    {
        Console.Error.WriteLine($"azpm: {ex.Message}");
        return ex.ExitCode;
    }
}

// Service-principal options, shared by `add` and `login`.
(Option<bool> Sp, Option<string?> ClientId, Option<string?> Secret, Option<bool> SecretStdin, Option<string?> Cert)
    SpOptions() => (
        new Option<bool>("--service-principal", "--sp") { Description = "Authenticate as a service principal" },
        new Option<string?>("--client-id") { Description = "Service principal application (client) ID" },
        new Option<string?>("--client-secret") { Description = "Service principal client secret" },
        new Option<bool>("--client-secret-stdin") { Description = "Read the client secret from stdin" },
        new Option<string?>("--certificate") { Description = "PEM certificate file (instead of a secret)" });

// --- add -------------------------------------------------------------------
var addName = new Argument<string>("name") { Description = "Profile name" };
var addTenant = new Option<string?>("--tenant", "-t") { Description = "Tenant ID or domain" };
var addDeviceCode = new Option<bool>("--device-code") { Description = "Use the device-code login flow" };
var addDescription = new Option<string?>("--description") { Description = "Free-text note shown in 'azpm ls'" };
var addSp = SpOptions();
var addCmd = new Command("add", "Create a profile and log in")
{
    addName, addTenant, addDeviceCode, addDescription,
    addSp.Sp, addSp.ClientId, addSp.Secret, addSp.SecretStdin, addSp.Cert,
};
addCmd.SetAction(r => Guard(() =>
{
    var tenant = r.GetValue(addTenant);
    var sp = ServicePrincipalInput.Resolve(
        r.GetValue(addSp.Sp), r.GetValue(addSp.ClientId), tenant,
        r.GetValue(addSp.Secret), r.GetValue(addSp.SecretStdin), r.GetValue(addSp.Cert), Console.In);
    return new AddHandler(Store(r), AzCli.Locate(), Console.Out).Run(
        r.GetValue(addName)!, new InteractiveLogin(tenant, r.GetValue(addDeviceCode)), sp, r.GetValue(addDescription));
}));
root.Subcommands.Add(addCmd);

// --- import ----------------------------------------------------------------
var importName = new Argument<string>("name") { Description = "New profile name" };
var importFrom = new Option<string?>("--from") { Description = "Source config dir (default: current az context, else ~/.azure)" };
var importCmd = new Command("import", "Turn an existing Azure CLI config dir into a profile") { importName, importFrom };
importCmd.SetAction(r => Guard(() => new ImportHandler(Store(r), Console.Out).Run(
    r.GetValue(importName)!, r.GetValue(importFrom))));
root.Subcommands.Add(importCmd);

// --- rename ---------------------------------------------------------------
var renameOld = new Argument<string>("old") { Description = "Current profile name" };
var renameNew = new Argument<string>("new") { Description = "New profile name" };
var renameCmd = new Command("rename", "Rename a profile") { renameOld, renameNew };
renameCmd.SetAction(r => Guard(() => new RenameHandler(Store(r), Console.Out).Run(
    r.GetValue(renameOld)!, r.GetValue(renameNew)!)));
root.Subcommands.Add(renameCmd);

// --- ls --------------------------------------------------------------------
var lsJson = new Option<bool>("--json") { Description = "Emit JSON instead of a table" };
var lsCheck = new Option<bool>("--check") { Description = "Probe each profile's token with 'az' (valid / needs login / timed out); slower" };
var lsCmd = new Command("ls",
    "List profiles: account, tenant, subscription, STATUS (ready/logged out), LOGIN (age of last sign-in)")
    { lsJson, lsCheck };
lsCmd.Aliases.Add("list");
lsCmd.SetAction(r => Guard(() => new LsHandler(Store(r), Console.Out, AzCli.Locate).Run(
    r.GetValue(lsJson), r.GetValue(lsCheck))));
root.Subcommands.Add(lsCmd);

// --- path -----------------------------------------------------------------
var pathName = new Argument<string>("name") { Description = "Profile name" };
var pathCmd = new Command("path", "Print a profile's AZURE_CONFIG_DIR") { pathName };
pathCmd.SetAction(r => Guard(() => new PathHandler(Store(r), Console.Out).Run(r.GetValue(pathName)!)));
root.Subcommands.Add(pathCmd);

// --- current ------------------------------------------------------------
var currentCmd = new Command("current",
    "Print the active profile (AZPM_PROFILE); exits non-zero if none (use 'azpm prompt' in prompts)");
currentCmd.SetAction(_ => Guard(() => new CurrentHandler(Console.Out, Console.Error).Run()));
root.Subcommands.Add(currentCmd);

// --- prompt ------------------------------------------------------------
var promptFormat = new Option<string?>("--format") { Description = "Template; {} is replaced by the profile name, e.g. \" [az:{}]\" (default: the bare name)" };
var promptCmd = new Command("prompt",
    "Active profile for a shell prompt: silent when none, always exits 0 (vs 'current', which errors)")
    { promptFormat };
promptCmd.SetAction(r => Guard(() => new PromptHandler(Console.Out).Run(r.GetValue(promptFormat))));
root.Subcommands.Add(promptCmd);

// --- exec --------------------------------------------------------------
var execName = new Argument<string>("name") { Description = "Profile name" };
var execCommand = new Argument<string[]>("command")
{
    Description = "Command and arguments to run (put after --)",
    Arity = ArgumentArity.ZeroOrMore,
};
var execCmd = new Command("exec",
    "Run one command in a profile, then return - everything after '--' runs verbatim: azpm exec prod -- az group list")
{
    execName, execCommand,
};
execCmd.TreatUnmatchedTokensAsErrors = false;
execCmd.SetAction(r => Guard(() =>
{
    // Everything after the first standalone "--" is the command, verbatim.
    var dd = Array.IndexOf(args, "--");
    var command = dd >= 0 ? args[(dd + 1)..] : r.GetValue(execCommand) ?? [];
    return new ExecHandler(Store(r)).Run(r.GetValue(execName)!, command);
}));
root.Subcommands.Add(execCmd);

// --- shell ------------------------------------------------------------
var shellName = new Argument<string>("name") { Description = "Profile name" };
var shellOpt = new Option<string?>("--shell") { Description = "pwsh | powershell | cmd | bash | zsh | fish" };
var shellCmd = new Command("shell",
    "Open an interactive subshell with the profile active ('exit' to leave). Zero setup - the safe default")
{
    shellName, shellOpt,
};
shellCmd.SetAction(r => Guard(() => new ShellHandler(Store(r), Console.Out, Console.Error).Run(
    r.GetValue(shellName)!, r.GetValue(shellOpt))));
root.Subcommands.Add(shellCmd);

// --- login -----------------------------------------------------------
var loginName = new Argument<string>("name") { Description = "Profile name" };
var loginTenant = new Option<string?>("--tenant", "-t") { Description = "Tenant ID or domain" };
var loginDeviceCode = new Option<bool>("--device-code") { Description = "Use the device-code login flow" };
var loginReset = new Option<bool>("--reset") { Description = "Clear the profile's existing login state first" };
var loginSp = SpOptions();
var loginCmd = new Command("login", "Re-authenticate an existing profile")
{
    loginName, loginTenant, loginDeviceCode, loginReset,
    loginSp.Sp, loginSp.ClientId, loginSp.Secret, loginSp.SecretStdin, loginSp.Cert,
};
loginCmd.SetAction(r => Guard(() =>
{
    var tenant = r.GetValue(loginTenant);
    var sp = ServicePrincipalInput.Resolve(
        r.GetValue(loginSp.Sp), r.GetValue(loginSp.ClientId), tenant,
        r.GetValue(loginSp.Secret), r.GetValue(loginSp.SecretStdin), r.GetValue(loginSp.Cert), Console.In);
    return new LoginHandler(Store(r), AzCli.Locate(), Console.Out).Run(
        r.GetValue(loginName)!, new InteractiveLogin(tenant, r.GetValue(loginDeviceCode)), sp, r.GetValue(loginReset));
}));
root.Subcommands.Add(loginCmd);

// --- logout ---------------------------------------------------------
var logoutName = new Argument<string>("name") { Description = "Profile name" };
var logoutCmd = new Command("logout", "Sign out of a profile (keeps the profile)") { logoutName };
logoutCmd.SetAction(r => Guard(() => new LogoutHandler(Store(r), AzCli.Locate(), Console.Out).Run(
    r.GetValue(logoutName)!)));
root.Subcommands.Add(logoutCmd);

// --- rm -------------------------------------------------------------
var rmName = new Argument<string>("name") { Description = "Profile name" };
var rmYes = new Option<bool>("--yes", "-y") { Description = "Don't prompt for confirmation" };
var rmCmd = new Command("rm", "Delete a profile and its login state") { rmName, rmYes };
rmCmd.Aliases.Add("remove");
rmCmd.SetAction(r => Guard(() => new RmHandler(Store(r), Console.In, Console.Out, Console.Error).Run(
    r.GetValue(rmName)!, r.GetValue(rmYes))));
root.Subcommands.Add(rmCmd);

// --- use -----------------------------------------------------------
var useName = new Argument<string>("name") { Description = "Profile name" };
var useShell = new Option<string?>("--shell") { Description = "pwsh | powershell | cmd | bash | zsh | fish" };
var useEmit = new Option<bool>("--emit") { Description = "Print only the eval-able script (used by 'azpm init')" };
var useCmd = new Command("use",
    "Switch THIS shell to a profile, no subshell (needs the one-time 'azpm init' setup line loaded)")
{
    useName, useShell, useEmit,
};
useCmd.SetAction(r => Guard(() => new UseHandler(Store(r), Console.Out, Console.Error).Run(
    r.GetValue(useName)!, r.GetValue(useShell), r.GetValue(useEmit))));
root.Subcommands.Add(useCmd);

// --- deactivate --------------------------------------------------
var deactShell = new Option<string?>("--shell") { Description = "pwsh | powershell | cmd | bash | zsh | fish" };
var deactEmit = new Option<bool>("--emit") { Description = "Print only the eval-able script (used by 'azpm init')" };
var deactCmd = new Command("deactivate",
    "Clear the profile from THIS shell (needs the 'azpm init' setup line, same as 'azpm use')")
    { deactShell, deactEmit };
deactCmd.SetAction(r => Guard(() => new DeactivateHandler(Console.Out).Run(r.GetValue(deactShell))));
root.Subcommands.Add(deactCmd);

// --- init ---------------------------------------------------------
var initShell = new Argument<string>("shell") { Description = "pwsh | powershell | bash | zsh | fish" };
var initAuto = new Option<bool>("--auto") { Description = "Also follow .azpm files on cd (only ones you've run 'azpm local allow' on)" };
var initFullAuto = new Option<bool>("--fullauto") { Description = "Like --auto but with no trust check — follows any .azpm, including from cloned repos" };
var initCmd = new Command("init",
    "Print the shell setup line for 'azpm use' / 'deactivate' - add it to your shell profile once")
    { initShell, initAuto, initFullAuto };
initCmd.SetAction(r => Guard(() => new InitHandler(Console.Out, Console.Error).Run(
    r.GetValue(initShell)!, r.GetValue(initAuto), r.GetValue(initFullAuto))));
root.Subcommands.Add(initCmd);

// --- local -------------------------------------------------------
var localName = new Argument<string?>("name") { Description = "Profile to pin to this directory", Arity = ArgumentArity.ZeroOrOne };
var localResolve = new Option<bool>("--resolve") { Description = "Print the resolved profile for the cwd (used by 'azpm init --auto')" };
var localTrustAll = new Option<bool>("--trust-all") { Description = "With --resolve: skip the trust check (used by 'azpm init --fullauto')" };
var localAllow = new Option<bool>("--allow") { Description = "Trust this directory's .azpm so 'azpm init --auto' will follow it" };
var localUnset = new Option<bool>("--unset") { Description = "Remove this directory's .azpm file (and its trust entry)" };
var localCmd = new Command("local",
    "Pin a profile to this directory tree via a .azpm file (bare: show; --allow: trust; --unset: remove)")
{
    localName, localResolve, localTrustAll, localAllow, localUnset,
};
localCmd.SetAction(r => Guard(() =>
{
    var h = new LocalHandler(Store(r), Console.Out, Console.Error);
    var n = r.GetValue(localName);
    if (r.GetValue(localResolve)) return h.Resolve(r.GetValue(localTrustAll));
    if (r.GetValue(localAllow)) return h.Allow();
    if (r.GetValue(localUnset)) return h.Unset();
    return n is null ? h.Show() : h.Set(n);
}));
root.Subcommands.Add(localCmd);

// --- portal ------------------------------------------------------
var portalName = new Argument<string?>("name") { Description = "Profile name", Arity = ArgumentArity.ZeroOrOne };
var portalPath = new Argument<string?>("path") { Description = "Portal path / blade (optional)", Arity = ArgumentArity.ZeroOrOne };
var portalBrowser = new Option<string?>("--browser") { Description = "Browser to launch: edge | chrome | brave | firefox | default (saved per profile)" };
var portalBrowserProfile = new Option<string?>("--browser-profile") { Description = "Browser-profile directory or shown name (saved per profile)" };
var portalListBrowsers = new Option<bool>("--browsers") { Description = "List the browser profiles azpm can see, then exit" };
var portalCmd = new Command("portal",
    "Open the Azure Portal as the profile's tenant, in a bound browser profile (--browsers to list; bind with --browser)")
{
    portalName, portalPath, portalBrowser, portalBrowserProfile, portalListBrowsers,
};
portalCmd.SetAction(r => Guard(() =>
{
    var h = new PortalHandler(Store(r), new UrlOpener(), Console.Out, Console.Error);
    if (r.GetValue(portalListBrowsers))
        return h.ListBrowsers();
    var pname = r.GetValue(portalName)
        ?? throw new AzpmException(ExitCode.UsageError, "usage: azpm portal <name>  (or: azpm portal --browsers)");
    return h.Run(pname, r.GetValue(portalPath), r.GetValue(portalBrowser), r.GetValue(portalBrowserProfile));
}));
root.Subcommands.Add(portalCmd);

// --- mcp --------------------------------------------------------
var mcpCmd = new Command("mcp", "Run a read-only MCP server (stdio) exposing profiles + read-only az");
mcpCmd.SetAction(r => Guard(() =>
{
    var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    return new McpHandler(Store(r), AzCli.Locate(), version).Run(Console.In, Console.Out);
}));

var mcpHideName = new Argument<string>("name") { Description = "Profile name" };
var mcpHideCmd = new Command("hide", "Hide a profile from 'azpm mcp' (drops it from the tool list and refuses azpm_az)")
    { mcpHideName };
mcpHideCmd.SetAction(r => Guard(() =>
    new McpVisibilityHandler(Store(r), Console.Out).Run(r.GetValue(mcpHideName)!, hide: true)));
mcpCmd.Subcommands.Add(mcpHideCmd);

var mcpShowName = new Argument<string>("name") { Description = "Profile name" };
var mcpShowCmd = new Command("show", "Make a hidden profile visible to 'azpm mcp' again") { mcpShowName };
mcpShowCmd.SetAction(r => Guard(() =>
    new McpVisibilityHandler(Store(r), Console.Out).Run(r.GetValue(mcpShowName)!, hide: false)));
mcpCmd.Subcommands.Add(mcpShowCmd);

root.Subcommands.Add(mcpCmd);

// --- bare `azpm` -------------------------------------------------
root.SetAction(r => Guard(() =>
{
    var store = Store(r);

    // Piped / no terminal: just show the profiles and point at --help.
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
    {
        new LsHandler(store, Console.Out, AzCli.Locate).Run(json: false);
        Console.Error.WriteLine("run 'azpm --help' for commands");
        return ExitCode.Ok;
    }

    // Terminal attached: pick a profile, then drop into its shell.
    var picked = new PickHandler(store, Console.In, Console.Out, Console.Error).Choose();
    return picked is null
        ? ExitCode.Ok
        : new ShellHandler(store, Console.Out, Console.Error).Run(picked, null);
}));

return root.Parse(args).Invoke();
