namespace WaNut.Core.Instances;

internal static class AtomicFile
{
    /// <summary>
    /// Writes to a temporary file first and then replaces the target, so a crash never leaves a half-written file.
    /// </summary>
    public static void WriteAllText(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
