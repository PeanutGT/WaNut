using WaNut.App.Resources;
using WaNut.Core.Worlds;

namespace WaNut.App.ViewModels;

/// <summary>The plain-language notes shown after an import or export.</summary>
internal static class WorldMessages
{
    public static string ImportNotes(WorldImportResult result)
    {
        var notes = new List<string>();
        if (result.InventoryProblem is { } problem)
        {
            notes.Add(Text.Get($"World_Inventory_{problem}"));
        }

        if (result.HasResourcePack)
        {
            notes.Add(Text.Get("World_ResourcePackHint"));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, notes);
    }

    public static string Problem(WorldException e) => Text.Get($"World_Problem_{e.Problem}");
}
