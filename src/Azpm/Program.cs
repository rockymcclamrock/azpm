using System.CommandLine;
using Azpm;
using Azpm.Handlers;

var homeOption = new Option<string?>("--home")
{
    Description = "Override the azpm home directory (default: AZPM_HOME or ~/.azpm)",
    Recursive = true,
};

var root = new RootCommand("azpm - Azure Profile Manager: isolated Azure CLI login profiles");
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

// --- ls --------------------------------------------------------------------
var lsJson = new Option<bool>("--json") { Description = "Emit JSON instead of a table" };
var lsCmd = new Command("ls", "List profiles") { lsJson };
lsCmd.Aliases.Add("list");
lsCmd.SetAction(r => Guard(() => new LsHandler(Store(r), Console.Out).Run(r.GetValue(lsJson))));
root.Subcommands.Add(lsCmd);

// --- path -----------------------------------------------------------------
var pathName = new Argument<string>("name") { Description = "Profile name" };
var pathCmd = new Command("path", "Print a profile's AZURE_CONFIG_DIR") { pathName };
pathCmd.SetAction(r => Guard(() => new PathHandler(Store(r), Console.Out).Run(r.GetValue(pathName)!)));
root.Subcommands.Add(pathCmd);

// --- current ------------------------------------------------------------
var currentCmd = new Command("current", "Print the active profile (from AZPM_PROFILE)");
currentCmd.SetAction(_ => Guard(() => new CurrentHandler(Console.Out, Console.Error).Run()));
root.Subcommands.Add(currentCmd);

// --- prompt ------------------------------------------------------------
var promptFormat = new Option<string?>("--format") { Description = "Template with {} for the profile name (default: just the name)" };
var promptCmd = new Command("prompt", "Prompt-friendly active-profile string (empty + exit 0 when none)") { promptFormat };
promptCmd.SetAction(r => Guard(() => new PromptHandler(Console.Out).Run(r.GetValue(promptFormat))));
root.Subcommands.Add(promptCmd);

// --- exec --------------------------------------------------------------
var execName = new Argument<string>("name") { Description = "Profile name" };
var execCommand = new Argument<string[]>("command")
{
    Description = "Command and arguments to run (put after --)",
    Arity = ArgumentArity.ZeroOrMore,
};
var execCmd = new Command("exec", "Run a command in a profile: azpm exec <name> -- <cmd> [args...]")
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
var shellCmd = new Command("shell", "Open an interactive subshell with the profile active")
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
var useCmd = new Command("use", "Point the current shell at a profile (eval its output, or use 'azpm init')")
{
    useName, useShell, useEmit,
};
useCmd.SetAction(r => Guard(() => new UseHandler(Store(r), Console.Out, Console.Error).Run(
    r.GetValue(useName)!, r.GetValue(useShell), r.GetValue(useEmit))));
root.Subcommands.Add(useCmd);

// --- deactivate --------------------------------------------------
var deactShell = new Option<string?>("--shell") { Description = "pwsh | powershell | cmd | bash | zsh | fish" };
var deactEmit = new Option<bool>("--emit") { Description = "Print only the eval-able script (used by 'azpm init')" };
var deactCmd = new Command("deactivate", "Clear the profile env from the current shell") { deactShell, deactEmit };
deactCmd.SetAction(r => Guard(() => new DeactivateHandler(Console.Out).Run(r.GetValue(deactShell))));
root.Subcommands.Add(deactCmd);

// --- init ---------------------------------------------------------
var initShell = new Argument<string>("shell") { Description = "pwsh | powershell | bash | zsh | fish" };
var initAuto = new Option<bool>("--auto") { Description = "Also add a hook that follows .azpm files on cd" };
var initCmd = new Command("init", "Print a shell wrapper enabling 'azpm use' / 'azpm deactivate'") { initShell, initAuto };
initCmd.SetAction(r => Guard(() => new InitHandler(Console.Out).Run(r.GetValue(initShell)!, r.GetValue(initAuto))));
root.Subcommands.Add(initCmd);

// --- local -------------------------------------------------------
var localName = new Argument<string?>("name") { Description = "Profile to pin to this directory", Arity = ArgumentArity.ZeroOrOne };
var localResolve = new Option<bool>("--resolve") { Description = "Print the resolved profile for the cwd (used by 'azpm init --auto')" };
var localUnset = new Option<bool>("--unset") { Description = "Remove this directory's .azpm file" };
var localCmd = new Command("local", "Pin a profile to the current directory tree via a .azpm file")
{
    localName, localResolve, localUnset,
};
localCmd.SetAction(r => Guard(() =>
{
    var h = new LocalHandler(Store(r), Console.Out, Console.Error);
    var n = r.GetValue(localName);
    if (r.GetValue(localResolve)) return h.Resolve();
    if (r.GetValue(localUnset)) return h.Unset();
    return n is null ? h.Show() : h.Set(n);
}));
root.Subcommands.Add(localCmd);

// --- portal ------------------------------------------------------
var portalName = new Argument<string>("name") { Description = "Profile name" };
var portalPath = new Argument<string?>("path") { Description = "Portal path / blade (optional)", Arity = ArgumentArity.ZeroOrOne };
var portalBrowser = new Option<string?>("--browser") { Description = "edge | chrome | firefox | default (persists)" };
var portalBrowserProfile = new Option<string?>("--browser-profile") { Description = "Browser-profile name / directory (persists)" };
var portalCmd = new Command("portal", "Open the Azure Portal in the profile's browser context")
{
    portalName, portalPath, portalBrowser, portalBrowserProfile,
};
portalCmd.SetAction(r => Guard(() => new PortalHandler(Store(r), new UrlOpener(), Console.Out, Console.Error).Run(
    r.GetValue(portalName)!, r.GetValue(portalPath), r.GetValue(portalBrowser), r.GetValue(portalBrowserProfile))));
root.Subcommands.Add(portalCmd);

return root.Parse(args).Invoke();
