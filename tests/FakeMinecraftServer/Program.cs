using System.Text;

// Prints Vanilla-style console lines and answers a few commands:
//   stop          prints "Stopping server" and exits 0 (ignored when started with --ignore-stop)
//   join <name>   prints "<name> joined the game"
//   leave <name>  prints "<name> left the game"
//   say <text>    prints "[Server] <text>"
//   crash         exits with code 3
var utf8 = new UTF8Encoding(false);
Console.InputEncoding = utf8;
Console.OutputEncoding = utf8;
var ignoreStop = args.Contains("--ignore-stop");

Log("Starting minecraft server version fake");
Console.Error.WriteLine("This line goes to stderr");
Log("Preparing level \"world\"");
Log("Done (0.123s)! For help, type \"help\"");

while (Console.ReadLine() is { } line)
{
    var parts = line.Split(' ', 2);
    var rest = parts.Length > 1 ? parts[1] : string.Empty;
    switch (parts[0])
    {
        case "stop" when !ignoreStop:
            Log("Stopping server");
            Log("Saving worlds");
            return 0;
        case "join":
            Log($"{rest} joined the game");
            break;
        case "leave":
            Log($"{rest} left the game");
            break;
        case "say":
            Log($"[Server] {rest}");
            break;
        case "crash":
            return 3;
        default:
            Log($"Unknown command: {line}");
            break;
    }
}

return 0;

static void Log(string message) => Console.WriteLine($"[12:00:00] [Server thread/INFO]: {message}");
