using Xunit;

// Several tests mutate process-global state (cwd, environment variables), so run serially.
// The suite is fast enough that this costs nothing.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
