namespace WaNut.Core.Remote.Files;

/// <summary>Why <see cref="WorkspaceRootGuard"/> refused a folder on the VPS.</summary>
public enum WorkspaceRootProblem
{
    /// <summary>The folder is "/", a system folder, or a folder that holds whole user homes, such as /home or /var.</summary>
    SystemFolder,

    /// <summary>The folder is the SSH user's home, a home in /home, or a folder above the user's home.</summary>
    HomeFolder,

    /// <summary>The folder has no server.properties, eula.txt or libraries/, so it doesn't look like a server folder.</summary>
    NoServerFiles,

    /// <summary>The folder became another one (for example, it was replaced by a link) after it was opened.</summary>
    Moved,
}
