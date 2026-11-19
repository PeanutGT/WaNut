namespace WaNut.Core.Hotplug;

/// <summary>What the console showed when a /reload went wrong.</summary>
/// <param name="Summary">One English sentence for the result banner and the audit file.</param>
/// <param name="Lines">The error lines (and the lines that continue them), as the console printed them.</param>
public sealed record ReloadFailure(string Summary, IReadOnlyList<string> Lines);
