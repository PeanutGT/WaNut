using WaNut.Core.Mods;

namespace WaNut.Core.Tests.Mods;

public sealed class MarkdownTextTests
{
    [Fact]
    public void ToPlain_DropsPicturesTagsAndMarks_KeepsLinkWords()
    {
        var markdown = "# Sodium\r\n\r\n![banner](https://x/b.png)\r\n<p align=\"center\">A **fast** [renderer](https://x).</p>\r\n\r\n\r\n- one\r\n* two\r\n\r\n---\r\nUse `F3` &amp; enjoy";

        var plain = MarkdownText.ToPlain(markdown);

        Assert.Equal("Sodium\n\nA fast renderer.\n\n• one\n• two\n\nUse F3 & enjoy", plain);
    }
}
