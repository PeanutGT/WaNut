namespace WaNut.Core.Settings.Config;

/// <summary>One choice of a <see cref="ConfigControlType.Dropdown"/>: the value written to the file and its text key.</summary>
public sealed record ConfigOption(string Value, string LocalizationKey);
