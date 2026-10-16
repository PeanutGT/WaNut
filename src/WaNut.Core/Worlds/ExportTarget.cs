namespace WaNut.Core.Worlds;

public enum ExportTarget
{
    /// <summary>A .zip file with the world folder inside, ready to share or import again.</summary>
    Zip,

    /// <summary>A new world in a launcher's saves folder, ready to open in single player.</summary>
    Saves,
}
