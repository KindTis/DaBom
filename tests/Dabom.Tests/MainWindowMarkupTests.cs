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

    private static string ReadMainWindowMarkup()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Dabom", "MainWindow.xaml"));
        return File.ReadAllText(path);
    }
}
