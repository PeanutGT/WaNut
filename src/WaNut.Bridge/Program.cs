using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WaNut.Bridge;
using WaNut.Core.Bridge;
using WaNut.Core.Http;
using WaNut.Core.Remote;

// Usage: WaNut.Bridge [--config <path>] [--log <path>], or WaNut.Bridge --status|--stop <server id>.
// The config defaults to bridge.config.json next to the executable; the log to standard output.
// Exit codes: 0 stopped, 1 crashed (systemd or the launcher restarts it), 2 the config is unusable,
// 3 no bridge answered --status or --stop.
var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
if (args is ["--version"])
{
    Console.WriteLine(version);
    return 0;
}

if (args is [("--status" or "--stop") and var ask, var askedId])
{
    var answer = await BridgeStatusPipe.AskAsync(askedId, ask == "--stop" ? BridgeStatusPipe.StopRequest : BridgeStatusPipe.StatusRequest);
    if (answer is null)
    {
        return 3;
    }

    Console.WriteLine(BridgeStatusPipe.Serialize(answer));
    return 0;
}

var options = BridgeArguments.Parse(args);
using var logFile = options.LogFile is null ? null : BridgeLogFile.Open(options.LogFile);
var log = new BridgeLog(logFile);
var configPath = options.ConfigFile ?? Path.Combine(AppContext.BaseDirectory, BridgeConfigJson.FileName);
BridgeConfig config;
try
{
    config = BridgeConfigJson.Deserialize(await File.ReadAllTextAsync(configPath));
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
{
    log.Error($"Cannot read {configPath}: {ex.Message}");
    return 2;
}

if (BridgeSecrets.IsProtected(config))
{
    try
    {
        config = OperatingSystem.IsWindows()
            ? BridgeSecrets.Unprotect(config, new DpapiProtector(BridgeSecrets.Entropy))
            : throw new CryptographicException("Encrypted secrets only work on the Windows account that wrote them.");
    }
    catch (CryptographicException ex)
    {
        log.Error($"Cannot decrypt the secrets in {configPath}: {ex.Message} Deploy the bridge again from WaNut.");
        return 2;
    }
}

var problems = BridgeConfigCheck.Problems(config);
foreach (var problem in problems)
{
    log.Error(problem);
}

if (problems.Count > 0)
{
    return 2;
}

using var stop = new CancellationTokenSource();
void OnSignal(PosixSignalContext context)
{
    context.Cancel = true;
    stop.Cancel();
}

using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
using var http = WaNutHttp.CreateClient(version);
http.Timeout = TimeSpan.FromSeconds(30);

log.Info($"WaNut bridge {version} starting for server {config.ServerId}.");
var engine = new BridgeEngine(config, http, log);
var pipe = new StatusPipeServer(config.ServerId, engine.Snapshot, stop.Cancel, log);
var pipeRun = Task.Run(() => pipe.RunAsync(stop.Token));
try
{
    await engine.RunAsync(stop.Token);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    // Asked to stop.
}
catch (Exception ex)
{
    log.Error($"The bridge stopped after an error: {ex}");
    return 1;
}
finally
{
    await stop.CancelAsync();
    try
    {
        await pipeRun;
    }
    catch (OperationCanceledException)
    {
        // Stopping.
    }
}

log.Info("Bridge stopped.");
return 0;
