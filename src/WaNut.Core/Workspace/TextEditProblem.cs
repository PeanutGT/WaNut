namespace WaNut.Core.Workspace;

/// <summary>Why a file can't be opened in the text editor.</summary>
public enum TextEditProblem
{
    Folder,
    TooLarge,
    NotText,
}
