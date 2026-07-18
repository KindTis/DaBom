using System.IO;

namespace Dabom.Tests;

[TestClass]
public sealed class MainWindowMarkupTests
{
    [TestMethod]
    public void WarningButton_ExposesExactAccessibleName()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(
            markup,
            "AutomationProperties.Name=\"{Binding Warnings.Count, Mode=OneWay, StringFormat='경고 {0}건'}\"");
    }

    [TestMethod]
    public void WarningPopup_ConstrainsScrollableList()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "<ListBox x:Name=\"WarningsList\" Grid.Row=\"1\"");
        StringAssert.Contains(markup, "ScrollViewer.VerticalScrollBarVisibility=\"Auto\"");
    }

    [TestMethod]
    public void FeaturedPlay_UsesReferencePrimaryAction()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "x:Key=\"PrimaryActionButtonStyle\"");
        StringAssert.Contains(markup, "Style=\"{StaticResource PrimaryActionButtonStyle}\"");
        StringAssert.Contains(markup, "Data=\"M 0 0 L 8 5 L 0 10 Z\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"재생하기\"");
    }

    [TestMethod]
    public void LibraryToolbar_ContainsSearchGuidanceAndSortLabel()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "x:Name=\"LibraryToolbar\"");
        StringAssert.Contains(markup, "Text=\"제목, 감독, 배우 이름으로 검색\"");
        StringAssert.Contains(markup, "Text=\"Ctrl K\"");
        StringAssert.Contains(markup, "Text=\"정렬\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"영상 검색\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"정렬\"");
    }

    [TestMethod]
    public void CardPopup_ShowsPosterAndReferenceMetadata()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "<ColumnDefinition Width=\"126\" />");
        StringAssert.Contains(markup, "Image Source=\"{Binding Poster}\"");
        StringAssert.Contains(markup, "Text=\"상영일\"");
        StringAssert.Contains(markup, "Text=\"주요 배우\"");
        StringAssert.Contains(markup, "Text=\"영상\"");
    }

    private static string ReadMainWindowMarkup()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Dabom", "MainWindow.xaml"));
        return File.ReadAllText(path);
    }
}
