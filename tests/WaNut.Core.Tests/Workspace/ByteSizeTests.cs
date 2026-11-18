using System.Globalization;
using WaNut.Core.Workspace;

namespace WaNut.Core.Tests.Workspace;

public class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024 / 2, "1.5 GB")]
    public void Format_PicksTheUnit(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Format(bytes, CultureInfo.InvariantCulture));
    }
}
