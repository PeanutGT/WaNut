namespace WaNut.Core.Packs;

/// <summary>One resource pack the server offers. Its place in the list is its priority: the first pack wins over later ones.</summary>
/// <param name="FileName">The zip in the server's resourcepacks folder.</param>
/// <param name="Url">The Modrinth download, used as is when this is the only pack.</param>
public sealed record ResourcePackEntry(string FileName, string ProjectId, string Title, string Url, string Sha1);
