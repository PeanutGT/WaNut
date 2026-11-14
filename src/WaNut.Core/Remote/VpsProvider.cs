namespace WaNut.Core.Remote;

/// <summary>
/// Which cloud a VPS runs on, read from its DMI fields (no network, no sudo). Only picks hint texts and guides; WaNut
/// never calls a cloud API.
/// </summary>
public enum VpsProvider
{
    Other,
    Oracle,
    Aws,
    Azure,
    Gcp,
}
