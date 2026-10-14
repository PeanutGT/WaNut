using WaNut.Core.Instances;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Instances;

public sealed class EulaFileTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void WriteAccepted_EmptyFolder_WritesEulaTrue()
    {
        EulaFile.WriteAccepted(_temp.Path, DateTimeOffset.UnixEpoch);

        Assert.Contains("eula=true", File.ReadAllLines(Path.Combine(_temp.Path, EulaFile.FileName)));
        Assert.True(EulaFile.IsAccepted(_temp.Path));
    }

    [Fact]
    public void IsAccepted_NoFile_ReturnsFalse()
    {
        Assert.False(EulaFile.IsAccepted(_temp.Path));
    }

    [Fact]
    public void IsAccepted_MinecraftDefaultFile_ReturnsFalse()
    {
        File.WriteAllText(Path.Combine(_temp.Path, EulaFile.FileName), "#comment\neula=false\n");

        Assert.False(EulaFile.IsAccepted(_temp.Path));
    }
}
