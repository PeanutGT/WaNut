namespace WaNut.Bridge.Tests;

public sealed class BridgeArgumentsTests
{
    [Fact]
    public void Parse_BothInAnyOrder_ReadsBoth()
    {
        Assert.Equal(new BridgeArguments("c.json", "b.log"), BridgeArguments.Parse(["--log", "b.log", "--config", "c.json"]));
    }

    [Fact]
    public void Parse_None_GivesNulls()
    {
        Assert.Equal(new BridgeArguments(null, null), BridgeArguments.Parse([]));
    }
}
