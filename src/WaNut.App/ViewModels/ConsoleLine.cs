using WaNut.Core.Hosting;

namespace WaNut.App.ViewModels;

/// <summary>One line in the live console, with its level read once so the row only picks a color.</summary>
public sealed record ConsoleLine(string Text, LogLineLevel Level);
