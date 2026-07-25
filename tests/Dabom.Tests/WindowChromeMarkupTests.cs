using System.IO;

namespace Dabom.Tests;

[TestClass]
public sealed class WindowChromeMarkupTests
{
    [TestMethod]
    public void VerticalScrollBar_UsesCompactSharedTemplateWithoutChangingHorizontal()
    {
        var theme = ReadThemeMarkup();

        StringAssert.Contains(theme, "<Style x:Key=\"ScrollBarPageButtonStyle\"");
        StringAssert.Contains(theme, "<Style x:Key=\"VerticalScrollThumbStyle\"");
        StringAssert.Contains(theme, "<Trigger Property=\"Orientation\" Value=\"Vertical\">");
        StringAssert.Contains(theme, "<Setter Property=\"Width\" Value=\"8\" />");
        StringAssert.Contains(theme, "Width=\"6\" MinHeight=\"32\"");
        StringAssert.Contains(theme, "CornerRadius=\"3\"");
        StringAssert.Contains(theme, "Background=\"{StaticResource MutedBrush}\"");
        StringAssert.Contains(theme, "Property=\"IsMouseOver\" Value=\"True\"");
        StringAssert.Contains(theme, "Property=\"IsDragging\" Value=\"True\"");
        StringAssert.Contains(theme, "Command=\"{x:Static ScrollBar.PageUpCommand}\"");
        StringAssert.Contains(theme, "Command=\"{x:Static ScrollBar.PageDownCommand}\"");
        Assert.IsFalse(theme.Contains("ScrollBar.LineUpCommand", StringComparison.Ordinal));
        Assert.IsFalse(theme.Contains("ScrollBar.LineDownCommand", StringComparison.Ordinal));
        Assert.IsFalse(
            theme.Contains(
                "<Trigger Property=\"Orientation\" Value=\"Horizontal\">",
                StringComparison.Ordinal));
    }

    private static string ReadThemeMarkup() =>
        File.ReadAllText(Path.Combine(
            ProjectDirectory(), "Styles", "DabomTheme.xaml"));

    private static string ProjectDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Dabom"));
}
