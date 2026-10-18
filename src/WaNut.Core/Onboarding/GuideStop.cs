namespace WaNut.Core.Onboarding;

/// <summary>One bubble of the guide. <see cref="Id"/> names its texts (Guide_&lt;Id&gt;Title and Guide_&lt;Id&gt;Text).</summary>
/// <param name="Scene">The peanut picture shown in the bubble.</param>
/// <param name="TabIndex">For <see cref="GuideTarget.Tab"/>, the tab's position on the main window; otherwise -1.</param>
public sealed record GuideStop(string Id, GuideTarget Target, string Scene, int TabIndex = -1);
