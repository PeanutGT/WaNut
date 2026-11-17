namespace WaNut.Core.Bridge;

/// <summary>How the bridge is kept running. <see cref="Auto"/> lets the deployer pick the best one the host supports.</summary>
public enum BridgeRunMode
{
    Auto,

    /// <summary>Local: a detached background process. Needs no admin rights, does not survive a reboot.</summary>
    DetachedProcess,

    /// <summary>Local: a user-level Task Scheduler task that starts at sign-in.</summary>
    ScheduledTask,

    /// <summary>Local: a Windows service. Needs admin rights once.</summary>
    WindowsService,

    /// <summary>VPS: a systemd service with restart-on-crash.</summary>
    Systemd,

    /// <summary>VPS without systemd: nohup in the background, no automatic restart.</summary>
    Nohup,

    /// <summary>VPS: a Docker container. Only when the user asks for it.</summary>
    Docker,
}
