using WaNut.Core.Versions;

namespace WaNut.Core.Instances;

public sealed record CreateServerRequest(string Name, GameVersion Version, int MemoryMb, bool EulaAccepted);
