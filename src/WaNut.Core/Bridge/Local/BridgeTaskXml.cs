using System.Xml.Linq;

namespace WaNut.Core.Bridge.Local;

/// <summary>
/// The Task Scheduler definition for A2: starts at the user's own sign-in, with the user's own rights (no UAC),
/// hidden, no time limit, on battery too, and started again a minute after a crash.
/// </summary>
public static class BridgeTaskXml
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>The task's name, in a WaNut folder of the Task Scheduler library.</summary>
    public static string TaskName(string serverId) => $@"WaNut\Discord bridge {BridgeStatusPipe.PipeName(serverId)}";

    /// <param name="userId">DOMAIN\user of the account that signs in.</param>
    /// <param name="commandLine">The arguments, already quoted for Windows.</param>
    /// <param name="label">Names the task in its description.</param>
    public static string Build(string userId, string executable, string commandLine, string label)
    {
        var task = new XElement(Ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Description", $"Keeps the WaNut Minecraft <-> Discord bridge running ({label}).")),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "LogonTrigger",
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "UserId", userId))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userId),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "LeastPrivilege"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "StartWhenAvailable", "true"),
                new XElement(Ns + "Hidden", "true"),
                new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(Ns + "IdleSettings",
                    new XElement(Ns + "StopOnIdleEnd", "false"),
                    new XElement(Ns + "RestartOnIdle", "false")),
                new XElement(Ns + "RestartOnFailure",
                    new XElement(Ns + "Interval", "PT1M"),
                    new XElement(Ns + "Count", "999"))),
            new XElement(Ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", executable),
                    new XElement(Ns + "Arguments", commandLine))));

        // schtasks reads the file as UTF-16, which is how it is written (see WindowsBridgeHost).
        return new XDeclaration("1.0", "UTF-16", null) + Environment.NewLine + task;
    }
}
