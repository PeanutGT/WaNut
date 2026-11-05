namespace WaNut.Core.Remote;

/// <summary>Why a <see cref="VpsProfile"/> can't be used yet.</summary>
public enum VpsProfileProblem
{
    NoName,
    BadHost,
    BadPort,
    NoUser,
    NoKeyFile,
}
