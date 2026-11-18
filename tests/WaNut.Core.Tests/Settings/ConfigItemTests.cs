using System.ComponentModel;
using WaNut.Core.Settings.Config;

namespace WaNut.Core.Tests.Settings;

public sealed class ConfigItemTests
{
    private static ConfigItem Item(string key, Dictionary<string, string>? store = null) =>
        new(ServerConfigCatalog.All.Single(m => m.Key == key), store ?? []);

    [Fact]
    public void BoolValue_Set_WritesRawValueAndBack()
    {
        var item = Item("pvp");
        item.BoolValue = false;

        Assert.Equal("false", item.RawValue);
        item.RawValue = "TRUE";
        Assert.True(item.BoolValue);
    }

    [Fact]
    public void RawValue_Set_RaisesChangeForEveryView()
    {
        var item = Item("pvp");
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.RawValue = "false";

        Assert.Contains(nameof(ConfigItem.BoolValue), changed);
        Assert.Contains(nameof(ConfigItem.IsModified), changed);
    }

    [Fact]
    public void RawValue_SameValue_RaisesNothing()
    {
        var item = Item("pvp");
        var raised = false;
        item.PropertyChanged += (_, _) => raised = true;

        item.RawValue = "true";

        Assert.False(raised);
    }

    [Fact]
    public void NumericValue_AboveMax_ClampsToMax()
    {
        var item = Item("view-distance");
        item.NumericValue = 99;

        Assert.Equal("32", item.RawValue);
    }

    [Fact]
    public void NumericValue_BelowMin_ClampsToMin()
    {
        var item = Item("view-distance");
        item.NumericValue = 0;

        Assert.Equal("3", item.RawValue);
    }

    [Fact]
    public void NumericValue_Fraction_Rounds()
    {
        var item = Item("spawn-protection");
        item.NumericValue = 7.6;

        Assert.Equal("8", item.RawValue);
    }

    [Fact]
    public void NumericValue_UnreadableRaw_FallsBackToDefault()
    {
        var item = Item("view-distance");
        item.RawValue = "abc";

        Assert.Equal(10, item.NumericValue);
    }

    [Fact]
    public void NumericValue_WorksUnderCommaCulture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var item = Item("max-players");
            item.NumericValue = 1500;

            Assert.Equal("1500", item.RawValue);
            Assert.Equal(1500, item.NumericValue);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Fact]
    public void IsModified_DifferentFromDefault_IsTrueUntilReset()
    {
        var item = Item("gamemode");
        Assert.False(item.IsModified);

        item.RawValue = "creative";
        Assert.True(item.IsModified);

        item.ResetToDefault();
        Assert.False(item.IsModified);
    }

    [Fact]
    public void ShouldShowWarning_OnlineModeOff_IsTrue()
    {
        var item = Item("online-mode");
        Assert.False(item.ShouldShowWarning);

        item.BoolValue = false;
        Assert.True(item.ShouldShowWarning);
    }

    [Fact]
    public void TwoItemsOnOneStore_ShareValue()
    {
        var store = new Dictionary<string, string>();
        var a = Item("pvp", store);
        var b = Item("pvp", store);

        a.BoolValue = false;

        Assert.Equal("false", b.RawValue);
    }
}
