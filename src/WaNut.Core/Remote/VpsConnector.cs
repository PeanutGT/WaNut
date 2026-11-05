using System.Net.Sockets;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace WaNut.Core.Remote;

/// <summary>Opens SSH connections to a <see cref="VpsProfile"/>, checking its host key and signing in with its saved secret.</summary>
public sealed class VpsConnector(SecretStore secrets)
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    /// <summary>"SHA256:..." in OpenSSH's own format, from SSH.NET's unpadded base64.</summary>
    public static string FormatFingerprint(string sha256Base64) => "SHA256:" + sha256Base64.TrimEnd('=');

    /// <summary>
    /// Whether to go on with a host that shows <paramref name="fingerprint"/>. Throws when it isn't the confirmed one,
    /// so the caller can ask the user (first connection) or warn them (changed key).
    /// </summary>
    public static void CheckHostKey(string? confirmed, string fingerprint)
    {
        if (confirmed is null)
        {
            throw new HostKeyException(fingerprint, changed: false);
        }

        if (!string.Equals(confirmed, fingerprint, StringComparison.Ordinal))
        {
            throw new HostKeyException(fingerprint, changed: true);
        }
    }

    /// <exception cref="HostKeyException">The host key isn't the one the user confirmed.</exception>
    /// <exception cref="VpsLoginException">Signing in failed.</exception>
    /// <exception cref="VpsUnreachableException">The VPS couldn't be reached.</exception>
    /// <param name="typedSecret">
    /// Secrets just typed in and not saved yet (null when none), so "Test connection" works before "Save". Anything
    /// it doesn't return comes from the <see cref="SecretStore"/>.
    /// </param>
    public async Task<SshClient> ConnectAsync(VpsProfile profile, Func<SecretKind, string?>? typedSecret = null, CancellationToken cancellationToken = default)
    {
        var info = new ConnectionInfo(profile.Host.Trim().Trim('[', ']'), profile.Port, profile.User.Trim(), CreateAuthentication(profile, typedSecret))
        {
            Timeout = ConnectTimeout,
        };
        var client = new SshClient(info);
        HostKeyException? hostKeyProblem = null;
        client.HostKeyReceived += (_, e) =>
        {
            var fingerprint = FormatFingerprint(e.FingerPrintSHA256);
            try
            {
                CheckHostKey(profile.HostKey, fingerprint);
                e.CanTrust = true;
            }
            catch (HostKeyException problem)
            {
                hostKeyProblem = problem;
                e.CanTrust = false;
            }
        };

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch (Exception e) when (hostKeyProblem is not null && e is SshConnectionException)
        {
            client.Dispose();
            throw hostKeyProblem;
        }
        catch (SshAuthenticationException e)
        {
            client.Dispose();
            throw new VpsLoginException(VpsLoginFailure.Rejected, e);
        }
        catch (Exception e) when (e is SocketException or SshConnectionException or SshOperationTimeoutException or ProxyException or TimeoutException)
        {
            client.Dispose();
            throw new VpsUnreachableException(e);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Runs <paramref name="command"/> and returns its standard output; a non-zero exit is an error.</summary>
    public static async Task<string> RunAsync(SshClient client, string command, CancellationToken cancellationToken = default)
    {
        using var ssh = client.CreateCommand(command, Encoding.UTF8);
        await ssh.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (ssh.ExitStatus is not 0)
        {
            throw new InvalidOperationException($"The VPS command failed ({ssh.ExitStatus}): {ssh.Error.Trim()}");
        }

        return ssh.Result;
    }

    private AuthenticationMethod[] CreateAuthentication(VpsProfile profile, Func<SecretKind, string?>? typedSecret)
    {
        string? Secret(SecretKind kind) => typedSecret?.Invoke(kind) ?? secrets.Load(profile.Id, kind);

        var user = profile.User.Trim();
        switch (profile.Login)
        {
            case VpsLogin.Password:
                var password = Secret(SecretKind.Password) ?? throw new VpsLoginException(VpsLoginFailure.SecretMissing);

                // Many hosts only offer keyboard-interactive for passwords, so answer its prompts with the same password.
                var interactive = new KeyboardInteractiveAuthenticationMethod(user);
                interactive.AuthenticationPrompt += (_, e) =>
                {
                    foreach (var prompt in e.Prompts)
                    {
                        prompt.Response = password;
                    }
                };
                return [new PasswordAuthenticationMethod(user, password), interactive];

            case VpsLogin.GeneratedKey:
                var key = Secret(SecretKind.GeneratedKey) ?? throw new VpsLoginException(VpsLoginFailure.SecretMissing);
                return [new PrivateKeyAuthenticationMethod(user, ReadKey(new MemoryStream(Encoding.UTF8.GetBytes(key)), null))];

            case VpsLogin.KeyFile:
                Stream file;
                try
                {
                    file = File.OpenRead(profile.KeyFile ?? string.Empty);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    throw new VpsLoginException(VpsLoginFailure.BadKeyFile, e);
                }

                using (file)
                {
                    var passphrase = Secret(SecretKind.KeyPassphrase);
                    return [new PrivateKeyAuthenticationMethod(user, ReadKey(file, passphrase))];
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(profile), profile.Login, null);
        }
    }

    private static PrivateKeyFile ReadKey(Stream stream, string? passphrase)
    {
        try
        {
            return string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(stream) : new PrivateKeyFile(stream, passphrase);
        }
        catch (Exception e) when (e is SshException or SshPassPhraseNullOrEmptyException or InvalidOperationException or ArgumentException or FormatException or NotSupportedException)
        {
            throw new VpsLoginException(VpsLoginFailure.BadKeyFile, e);
        }
    }
}
