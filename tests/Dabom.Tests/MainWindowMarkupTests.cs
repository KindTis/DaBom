using System.IO;

namespace Dabom.Tests;

[TestClass]
public sealed class MainWindowMarkupTests
{
    [TestMethod]
    public void MainWindow_StartsCenteredOnScreen()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "WindowStartupLocation=\"CenterScreen\"");
    }

    [TestMethod]
    public void AppIcon_IsMultiResolutionAndUsedEverywhere()
    {
        var projectDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Dabom"));
        var project = File.ReadAllText(Path.Combine(projectDirectory, "Dabom.csproj"));
        var mainWindow = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.xaml"));
        var metadataWindow = File.ReadAllText(Path.Combine(projectDirectory, "MetadataWindow.xaml"));
        var iconPath = Path.Combine(projectDirectory, "Assets", "Dabom.ico");

        StringAssert.Contains(project, "<ApplicationIcon>Assets\\Dabom.ico</ApplicationIcon>");
        StringAssert.Contains(project, "<Resource Include=\"Assets\\Dabom.ico\" />");
        StringAssert.Contains(mainWindow, "Icon=\"Assets/Dabom.ico\"");
        StringAssert.Contains(metadataWindow, "Icon=\"Assets/Dabom.ico\"");
        Assert.IsTrue(File.Exists(iconPath), "다중 해상도 ICO가 생성되어야 합니다.");

        var bytes = File.ReadAllBytes(iconPath);
        Assert.IsTrue(bytes.Length >= 6, "ICO 헤더가 필요합니다.");
        Assert.AreEqual((ushort)0, BitConverter.ToUInt16(bytes, 0));
        Assert.AreEqual((ushort)1, BitConverter.ToUInt16(bytes, 2));

        var expectedSizes = new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var frameCount = BitConverter.ToUInt16(bytes, 4);
        Assert.AreEqual(expectedSizes.Length, frameCount);
        Assert.IsTrue(bytes.Length >= 6 + frameCount * 16, "ICO 디렉터리가 완전해야 합니다.");

        var actualSizes = new int[frameCount];
        for (var index = 0; index < frameCount; index++)
        {
            var entryOffset = 6 + index * 16;
            actualSizes[index] = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
            var height = bytes[entryOffset + 1] == 0 ? 256 : bytes[entryOffset + 1];
            Assert.AreEqual(actualSizes[index], height);
        }

        CollectionAssert.AreEquivalent(expectedSizes, actualSizes);
    }

    [TestMethod]
    public void RoundedElements_UseSharedCornerRadiusScale()
    {
        var markup = ReadMainWindowMarkup();
        var theme = ReadThemeMarkup();

        StringAssert.Contains(theme, "<CornerRadius x:Key=\"ControlCornerRadius\">12</CornerRadius>");
        StringAssert.Contains(theme, "<CornerRadius x:Key=\"SurfaceCornerRadius\">16</CornerRadius>");
        StringAssert.Contains(theme, "<CornerRadius x:Key=\"HeroCornerRadius\">28</CornerRadius>");
        StringAssert.Contains(theme, "x:Name=\"ButtonBorder\"");
        StringAssert.Contains(theme, "x:Name=\"TextBoxBorder\"");
        StringAssert.Contains(theme, "x:Name=\"ItemBorder\"");
        StringAssert.Contains(markup, "CornerRadius=\"{StaticResource ControlCornerRadius}\"");
        StringAssert.Contains(markup, "CornerRadius=\"{StaticResource SurfaceCornerRadius}\"");
        StringAssert.Contains(markup, "CornerRadius=\"{StaticResource HeroCornerRadius}\"");
        Assert.IsFalse(markup.Contains("CornerRadius=\"999\"", StringComparison.Ordinal));
    }

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
        StringAssert.Contains(theme, "CornerRadius=\"{StaticResource ControlCornerRadius}\"");
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

    [TestMethod]
    public void RoundedSurfaces_ClipContentInsideSeparateShadowHosts()
    {
        var markup = ReadMainWindowMarkup();
        var code = ReadMainWindowCode();

        StringAssert.Contains(markup, "x:Name=\"FeaturedHeroSurface\"");
        StringAssert.Contains(markup, "x:Name=\"CardPopupSurface\"");
        Assert.AreEqual(
            2,
            markup.Split("SizeChanged=\"OnRoundedClipSizeChanged\"", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(code, "private void OnRoundedClipSizeChanged");
        StringAssert.Contains(
            code,
            "child.Clip = new RectangleGeometry(new Rect(child.RenderSize), radius, radius)");
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
