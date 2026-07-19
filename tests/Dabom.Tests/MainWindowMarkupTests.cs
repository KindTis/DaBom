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
        StringAssert.Contains(markup, "Width=\"15\" Height=\"15\"");
        StringAssert.Contains(markup, "x:Name=\"PrimaryButtonTranslate\"");
        StringAssert.Contains(markup, "x:Name=\"PrimaryButtonShadow\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"재생하기\"");
    }

    [TestMethod]
    public void PageAndFeaturedVisuals_UseReferenceDepthLayers()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "x:Name=\"AmbientBaseLayer\"");
        StringAssert.Contains(markup, "x:Name=\"AmbientTopGlow\"");
        StringAssert.Contains(markup, "x:Name=\"FeaturedBackdrop\"");
        StringAssert.Contains(markup, "<BlurEffect Radius=\"14\" />");
    }

    [TestMethod]
    public void AmbientBackground_AvoidsLargeSoftwareBlurEffects()
    {
        var markup = ReadMainWindowMarkup();

        Assert.IsFalse(markup.Contains("<BlurEffect Radius=\"68\" />", StringComparison.Ordinal));
        Assert.IsFalse(markup.Contains("<BlurEffect Radius=\"72\" />", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FeaturedHero_KeepsReferenceMinimumWithoutBackdropExpansion()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(
            markup,
            "<Border x:Name=\"FeaturedHero\" Grid.Row=\"1\" Margin=\"0,28,0,0\" MinHeight=\"392\"");
        var backdropStart = markup.IndexOf("<Image x:Name=\"FeaturedBackdrop\"", StringComparison.Ordinal);
        var backdropEnd = markup.IndexOf("IsHitTestVisible=\"False\">", backdropStart, StringComparison.Ordinal);
        StringAssert.Contains(markup[backdropStart..backdropEnd], "Height=\"392\"");
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
    public void SortControl_UsesSingleRoundedReferenceTemplate()
    {
        var markup = ReadMainWindowMarkup();
        var theme = ReadThemeMarkup();

        StringAssert.Contains(markup, "Style=\"{StaticResource ReferenceSortComboBoxStyle}\"");
        Assert.IsFalse(
            markup.Contains(
                "<Border CornerRadius=\"999\" Background=\"{StaticResource RaisedBrush}\"",
                StringComparison.Ordinal));
        StringAssert.Contains(theme, "x:Key=\"ReferenceSortComboBoxStyle\"");
        StringAssert.Contains(theme, "CornerRadius=\"20\"");
    }

    [TestMethod]
    public void PosterFrames_DrawPosterAsRoundedBackground()
    {
        var markup = ReadMainWindowMarkup();

        Assert.IsTrue(
            markup.Split("ImageBrush ImageSource=\"{Binding Poster}\"").Length >= 4,
            "히어로, 카드, 툴팁 포스터가 각각 둥근 Border 배경으로 그려져야 합니다.");
    }

    [TestMethod]
    public void CardPopup_ShowsPosterAndReferenceMetadata()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "<ColumnDefinition Width=\"126\" />");
        StringAssert.Contains(markup, "Text=\"{Binding ReleaseDateText}\"");
        Assert.IsTrue(
            markup.Split("Text=\"{Binding DurationText, Mode=OneWay}\"").Length >= 3,
            "히어로와 툴팁 모두 읽기 전용 재생시간을 OneWay로 바인딩해야 합니다.");
        StringAssert.Contains(markup, "Text=\" · 1개 파일\"");
        StringAssert.Contains(markup, "<ColumnDefinition Width=\"58\" />");
        StringAssert.Contains(markup, "BorderThickness=\"0,1,0,0\"");
        StringAssert.Contains(markup, "Text=\"상영일\"");
        StringAssert.Contains(markup, "Text=\"주요 배우\"");
        StringAssert.Contains(markup, "Text=\"영상\"");
    }

    [TestMethod]
    public void CardPopup_FollowsPointerAndKeepsKeyboardPlacement()
    {
        var markup = ReadMainWindowMarkup();
        var code = ReadMainWindowCode();

        StringAssert.Contains(markup, "<EventSetter Event=\"MouseMove\" Handler=\"OnCardMove\" />");
        StringAssert.Contains(code, "private void OnCardMove");
        StringAssert.Contains(code, "CardPopup.Placement = PlacementMode.RelativePoint;");
        StringAssert.Contains(code, "CardPopup.PlacementRectangle = new Rect(");
        StringAssert.Contains(code, "CardPopup.Placement = PlacementMode.Right;");
    }

    private static string ReadMainWindowMarkup()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Dabom", "MainWindow.xaml"));
        return File.ReadAllText(path);
    }

    private static string ReadThemeMarkup()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Dabom", "Styles", "DabomTheme.xaml"));
        return File.ReadAllText(path);
    }

    private static string ReadMainWindowCode()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Dabom", "MainWindow.xaml.cs"));
        return File.ReadAllText(path);
    }
}
