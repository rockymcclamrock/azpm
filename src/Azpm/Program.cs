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

// --- add ---------------------------------------------------------------------
var addName = new Argument<string>("name") { Description = "Profile name" };
var addTenant = new Option<string?>("--tenant", "-t") { Description = "Tenant ID or domain" };
var addDeviceCode = new Option<bool>("--device-code") { Description = "Use the device-code login flow" };
var addDescription = new Option<string?>("--description") { Description = "Free-text note shown in 'azpm ls'" };
var addCmd = new Command("add", "Create a profile and log in") { addName, addTenant, addDeviceCode, addDescription };
addCmd.SetAction(r => new AddHandler(Store(r), AzCli.Locate(), Console.Out).Run(
    r.GetValue(addName)!, r.GetValue(addTenant), r.GetValue(addDeviceCode), r.GetValue(addDescription)));
root.Subcommands.Add(addCmd);

// --- ls ----------------------------------------------------------------------
var lsJson = new Option<bool>("--json") { Description = "Emit JSON instead of a table" };
var lsCmd = new Command("ls", "List profiles") { lsJson };
lsCmd.Aliases.Add("list");
lsCmd.SetAction(r => new LsHandler(Store(r), Console.Out).Run(r.GetValue(lsJson)));
root.Subcommands.Add(lsCmd);

// --- dispatch --------------------------------------------------------------
try
{
    return root.Parse(args).Invoke();
}
catch (AzpmException ex)
{
    Console.Error.WriteLine($"azpm: {ex.Message}");
    return ex.ExitCode;
}
