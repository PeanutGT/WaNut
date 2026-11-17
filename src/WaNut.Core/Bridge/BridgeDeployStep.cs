namespace WaNut.Core.Bridge;

/// <summary>The stages of a deployment, reported as progress. A failure in any stage undoes what earlier ones changed.</summary>
public enum BridgeDeployStep
{
    Validate,
    Probe,
    PatchServerProperties,
    CopyFiles,
    Register,
    HealthCheck,
}
