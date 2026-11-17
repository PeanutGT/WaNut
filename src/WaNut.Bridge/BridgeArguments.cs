namespace WaNut.Bridge;

/// <summary>The run options: <c>--config &lt;path&gt;</c> and <c>--log &lt;path&gt;</c>, in any order. Unknown words are ignored.</summary>
public sealed record BridgeArguments(string? ConfigFile, string? LogFile)
{
    public static BridgeArguments Parse(IReadOnlyList<string> args)
    {
        string? config = null, log = null;
        for (var i = 0; i + 1 < args.Count; i++)
        {
            switch (args[i])
            {
                case "--config":
                    config = args[++i];
                    break;
                case "--log":
                    log = args[++i];
                    break;
            }
        }

        return new BridgeArguments(config, log);
    }
}
