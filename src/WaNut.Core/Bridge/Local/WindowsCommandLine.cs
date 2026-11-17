using System.Text;

namespace WaNut.Core.Bridge.Local;

/// <summary>Joins arguments the way Windows programs split them again (CommandLineToArgvW rules).</summary>
public static class WindowsCommandLine
{
    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => c is ' ' or '\t' or '"'))
        {
            return argument;
        }

        var text = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            // Backslashes count only before a quote: double them there, and escape the quote.
            text.Append('\\', c == '"' ? (slashes * 2) + 1 : slashes).Append(c);
            slashes = 0;
        }

        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
}
