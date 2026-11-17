namespace WaNut.Core.Bridge;

/// <summary>Finds the bridge executable for a platform.</summary>
public interface IBridgeBinarySource
{
    /// <exception cref="BridgeDeployException"><see cref="BridgeDeployError.BinaryUnavailable"/>: this app has no build for it.</exception>
    Task<BridgeBinary> GetAsync(string rid, CancellationToken cancellationToken = default);

    /// <summary>Removes copies made earlier that no path in <paramref name="keep"/> is in; one in use stays.</summary>
    void DeleteOthers(IReadOnlyCollection<string> keep);
}
