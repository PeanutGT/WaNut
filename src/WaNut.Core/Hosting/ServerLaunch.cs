namespace WaNut.Core.Hosting;

/// <summary>Everything needed to start a server process.</summary>
public sealed record ServerLaunch(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);
