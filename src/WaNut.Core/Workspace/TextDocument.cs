namespace WaNut.Core.Workspace;

/// <summary>A text file as it was when opened; <see cref="SizeBytes"/> and <see cref="ModifiedUtc"/> tell a later save whether somebody else changed it meanwhile.</summary>
public sealed record TextDocument(string Path, string Text, long SizeBytes, DateTime ModifiedUtc, bool CrLf);

public enum TextSaveOutcome
{
    Saved,

    /// <summary>The file changed on the server after it was opened; nothing was written.</summary>
    ChangedMeanwhile,
}

public sealed record TextSaveResult(TextSaveOutcome Outcome, TextDocument? Saved);
