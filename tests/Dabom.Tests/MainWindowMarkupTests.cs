using Dabom.Library;
using Dabom.Main;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

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
    public void MainWindow_UsesCapturedDefaultSize()
    {
        var window = XDocument.Parse(ReadMainWindowMarkup()).Root!;

        Assert.AreEqual("1467", (string?)window.Attribute("Width"));
        Assert.AreEqual("1000", (string?)window.Attribute("Height"));
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
    public void LibraryFooter_StaysOutsideScrollableContent()
    {
        var document = XDocument.Parse(ReadMainWindowMarkup());
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var layout = document
            .Descendants(presentation + "Grid")
            .SingleOrDefault(element =>
                (string?)element.Attribute(x + "Name") == "MainContentLayout");

        Assert.IsNotNull(layout, "고정 하단 영역을 위한 레이아웃 Grid가 필요합니다.");
        var rowHeights = layout
            .Element(presentation + "Grid.RowDefinitions")!
            .Elements(presentation + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height"))
            .ToArray();
        CollectionAssert.AreEqual(new[] { "*", "Auto" }, rowHeights);

        var scroller = layout.Elements(presentation + "ScrollViewer").Single();
        var footer = layout
            .Elements(presentation + "Grid")
            .Single(element =>
                (string?)element.Attribute(x + "Name") == "LibraryFooter");
        Assert.AreEqual("0", (string?)scroller.Attribute("Grid.Row"));
        Assert.AreEqual("1", (string?)footer.Attribute("Grid.Row"));
    }

    [TestMethod]
    public void MainScrollBar_ReachesWindowEdgeWhileContentKeepsPageMargin()
    {
        var document = XDocument.Parse(ReadMainWindowMarkup());
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var layout = document
            .Descendants(presentation + "Grid")
            .Single(element =>
                (string?)element.Attribute(x + "Name") == "MainContentLayout");
        var scroller = layout.Elements(presentation + "ScrollViewer").Single();
        var content = scroller.Elements(presentation + "Grid").Single();
        var footer = layout
            .Elements(presentation + "Grid")
            .Single(element =>
                (string?)element.Attribute(x + "Name") == "LibraryFooter");

        Assert.IsNull(layout.Attribute("Margin"));
        Assert.IsNull(layout.Attribute("MaxWidth"));
        Assert.AreEqual("Stretch", (string?)scroller.Attribute("HorizontalContentAlignment"));
        Assert.AreEqual("32,18,32,0", (string?)content.Attribute("Margin"));
        Assert.AreEqual("1720", (string?)content.Attribute("MaxWidth"));
        Assert.AreEqual("32,0,32,0", (string?)footer.Attribute("Margin"));
        Assert.AreEqual("1720", (string?)footer.Attribute("MaxWidth"));
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
        StringAssert.Contains(markup, "<Button x:Name=\"SortDirectionButton\"");
        StringAssert.Contains(markup, "Command=\"{Binding ToggleSortDirectionCommand}\"");
        StringAssert.Contains(markup, "Value=\"정렬 방향: 오름차순\"");
        StringAssert.Contains(markup, "Value=\"정렬 방향: 내림차순\"");
    }

    [TestMethod]
    public void LibraryGrid_BindsTypedVideoAndSeasonItemsWithAccessibleSeasonAction()
    {
        var markup = ReadMainWindowMarkup();
        var videoList = XDocument.Parse(markup).Descendants()
            .Single(element => element.Name.LocalName == "ListBox"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Name"
                    && attribute.Value == "VideoList"));

        StringAssert.Contains(markup, "ItemsSource=\"{Binding VisibleItems}\"");
        StringAssert.Contains(markup, "SelectedItem=\"{Binding SelectedItem}\"");
        StringAssert.Contains(markup, "SelectionMode=\"Extended\"");
        StringAssert.Contains(markup, "SelectionChanged=\"OnVideoSelectionChanged\"");
        StringAssert.Contains(
            markup,
            "Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}");
        Assert.IsFalse(videoList.Elements().Any(element =>
            element.Name.LocalName == "ListBox.Style"));
        StringAssert.Contains(
            markup,
            "DataType=\"{x:Type main:VideoItemViewModel}\"");
        StringAssert.Contains(
            markup,
            "DataType=\"{x:Type main:SeasonItemViewModel}\"");
        StringAssert.Contains(markup, "x:Name=\"SeasonTypeRibbon\"");
        StringAssert.Contains(markup, "Text=\"TV 시즌\"");
        StringAssert.Contains(markup, "Text=\"{Binding Summary}\"");
        StringAssert.Contains(markup, "Property=\"AutomationProperties.Name\"");
        StringAssert.Contains(markup, "Value=\"{Binding AutomationName}\"");
    }

    [TestMethod]
    public void MissingFileCardsDimOnlyPosterAndKeepStatusRibbonOpaque()
    {
        var markup = ReadMainWindowMarkup();
        var videoTemplate = CardTemplate(
            markup,
            "<DataTemplate DataType=\"{x:Type main:VideoItemViewModel}\"",
            "<DataTemplate DataType=\"{x:Type main:SeasonItemViewModel}\"");
        var seasonTemplate = CardTemplate(
            markup,
            "<DataTemplate DataType=\"{x:Type main:SeasonItemViewModel}\"",
            "</ListBox.Resources>");

        Assert.AreEqual(2, markup.Split("Opacity=\"{Binding PosterOpacity}\"", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(videoTemplate, "Visibility=\"{Binding IsFileMissing,");
        StringAssert.Contains(videoTemplate, "Text=\"파일 없음\"");
        StringAssert.Contains(seasonTemplate, "x:Name=\"SeasonTypeRibbon\"");
        StringAssert.Contains(seasonTemplate, "Visibility=\"{Binding ContainsMissingFiles,");
        StringAssert.Contains(seasonTemplate, "Text=\"파일 없음 포함\"");

        AssertPosterPrecedesOpaqueMissingRibbon(videoTemplate);
        AssertPosterPrecedesOpaqueMissingRibbon(seasonTemplate);
    }

    [TestMethod]
    public void MissingFileCleanup_PrecedesRescanInLibraryFooter()
    {
        var document = XDocument.Parse(ReadMainWindowMarkup());
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var footer = document.Descendants(presentation + "Grid").Single(element =>
            (string?)element.Attribute(x + "Name") == "LibraryFooter");
        var buttons = footer.Descendants(presentation + "Button").ToArray();
        var cleanup = buttons.SingleOrDefault(element =>
            (string?)element.Attribute("Content") == "파일 없음 정리");
        var rescan = buttons.Single(element =>
            (string?)element.Attribute("Content") == "폴더 다시 확인");

        Assert.IsNotNull(cleanup, "파일 없음 정리 버튼이 필요합니다.");
        Assert.AreEqual(
            Array.IndexOf(buttons, rescan) - 1,
            Array.IndexOf(buttons, cleanup));
        Assert.AreEqual(
            "{Binding CanCleanMissingVideos}",
            (string?)cleanup.Attribute("IsEnabled"));
        Assert.AreEqual("OnCleanMissingVideos", (string?)cleanup.Attribute("Click"));
    }

    [TestMethod]
    public void DeleteKey_SnapshotsDisplayOrderAndReselectsOnlyFailedVideos()
    {
        var code = ReadMainWindowCode();
        var handler = MethodBody(
            code,
            "private async void OnVideoListKeyDown",
            "private void OnReturnToLibrary");
        var preview = MethodBody(
            code,
            "private void OnPreviewKeyDown",
            "private void OnMainScrollChanged");

        StringAssert.Contains(handler, "e.Key == Key.Delete");
        StringAssert.Contains(handler, "VideoList.Items.Cast<LibraryItemViewModel>()");
        StringAssert.Contains(handler, "VideoList.SelectedItems.Contains(item)");
        StringAssert.Contains(handler, "PrepareVideoDeletions(selectedItems)");
        StringAssert.Contains(handler, "preparation.Requests");
        StringAssert.Contains(handler, "preparation.Failures.Count");
        StringAssert.Contains(handler, "ShowDialog() != true");
        StringAssert.Contains(handler, "await viewModel.DeleteVideosAsync(preparation)");
        StringAssert.Contains(handler, "VideoList.UnselectAll();");
        StringAssert.Contains(handler, "selectedItems.OfType<VideoItemViewModel>()");
        StringAssert.Contains(handler, "result.FailedVideos.ToHashSet()");
        StringAssert.Contains(handler, "failedVideos.Contains(video)");
        StringAssert.Contains(handler, "VideoList.Items.Contains(video)");
        StringAssert.Contains(handler, "VideoList.SelectedItems.Add(video);");
        Assert.IsTrue(
            handler.IndexOf("ShowDialog() != true", StringComparison.Ordinal)
            < handler.IndexOf("VideoList.UnselectAll();", StringComparison.Ordinal),
            "취소 반환은 선택 해제보다 먼저여야 합니다.");
        Assert.IsFalse(handler.Contains("PrepareVideoDeletion()", StringComparison.Ordinal));
        Assert.IsFalse(handler.Contains("DeleteVideoAsync(", StringComparison.Ordinal));
        Assert.IsFalse(preview.Contains("Key.Delete", StringComparison.Ordinal));
    }

    [TestMethod]
    public void VideoDeletionConfirmationWindow_UsesScrollableAccessibleBatchLayout()
    {
        var markup = ReadVideoDeletionConfirmationMarkup();

        StringAssert.Contains(markup, "Title=\"영상 삭제\"");
        StringAssert.Contains(markup, "Text=\"영상을 삭제할까요?\"");
        StringAssert.Contains(markup, "x:Name=\"ExclusionText\"");
        StringAssert.Contains(markup, "x:Name=\"DeletionItems\"");
        StringAssert.Contains(markup, "MaxHeight=\"320\"");
        StringAssert.Contains(markup, "VerticalScrollBarVisibility=\"Auto\"");
        StringAssert.Contains(markup, "Text=\"{Binding FileName}\"");
        StringAssert.Contains(markup, "Text=\"{Binding FolderHint}\"");
        StringAssert.Contains(markup, "Text=\"{Binding ActionText}\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"{Binding AutomationName}\"");
        StringAssert.Contains(markup, "Content=\"선택 항목 삭제\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"선택 항목 삭제\"");
        StringAssert.Contains(markup, "IsCancel=\"True\"");
        StringAssert.Contains(markup, "IsDefault=\"True\"");
        StringAssert.Contains(
            markup,
            "FocusManager.FocusedElement=\"{Binding ElementName=CancelButton}\"");
    }

    [STATestMethod]
    [DoNotParallelize]
    public void VideoDeletionConfirmationWindow_DistinguishesOnlyDuplicateFileNames()
    {
        EnsureApplicationResources();
        var root = Directory.CreateTempSubdirectory("dabom-delete-dialog-many-");
        var store = new LibraryStore(root.FullName);
        var requests = new[]
        {
            DeletionRequest(
                store,
                Path.Combine(root.FullName, "A", "Common", "Movie.mkv"),
                VideoFileStatus.Present),
            DeletionRequest(
                store,
                Path.Combine(root.FullName, "B", "Common", "Movie.mkv"),
                VideoFileStatus.Missing),
            DeletionRequest(
                store,
                Path.Combine(root.FullName, "Unique.mkv"),
                VideoFileStatus.Present)
        };
        var window = new VideoDeletionConfirmationWindow(requests, 2);

        try
        {
            var items = ((ItemsControl)window.FindName("DeletionItems"))
                .Items
                .Cast<VideoDeletionConfirmationItem>()
                .ToArray();
            Assert.AreEqual(3, items.Length);
            Assert.AreEqual(Path.Combine("A", "Common"), items[0].FolderHint);
            Assert.AreEqual(Path.Combine("B", "Common"), items[1].FolderHint);
            Assert.IsNull(items[2].FolderHint);
            Assert.AreEqual(
                "휴지통으로 이동하고 목록에서 제거",
                items[0].ActionText);
            Assert.AreEqual("목록에서만 제거", items[1].ActionText);
            StringAssert.Contains(items[0].AutomationName, items[0].FolderHint);
            Assert.IsFalse(items[2].AutomationName.Contains(root.FullName));

            var exclusion = (TextBlock)window.FindName("ExclusionText");
            Assert.AreEqual(
                "2개 항목은 파일 상태를 확인하지 못해 제외됩니다.",
                exclusion.Text);
            Assert.AreEqual(Visibility.Visible, exclusion.Visibility);
            Assert.AreEqual(
                "선택 항목 삭제",
                ((Button)window.FindName("ConfirmButton")).Content);
        }
        finally
        {
            window.Close();
            root.Delete(true);
        }
    }

    [TestMethod]
    public void SeasonHero_ExposesContextAndAccessibleReturnControl()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "x:Name=\"SeasonHeroContent\"");
        StringAssert.Contains(markup, "DataContext=\"{Binding HeroVideo}\"");
        StringAssert.Contains(markup, "Content=\"← 전체 영상\"");
        StringAssert.Contains(
            markup,
            "AutomationProperties.Name=\"전체 영상으로 돌아가기\"");
        StringAssert.Contains(markup, "Text=\"{Binding SeasonHeading}\"");
        StringAssert.Contains(markup, "Text=\"{Binding ActiveSeason.IntroLabel}\"");
        StringAssert.Contains(markup, "Text=\"{Binding ActiveSeason.IntroHeading}\"");
        StringAssert.Contains(markup, "Command=\"{Binding PlayFeaturedCommand}\"");
    }

    [TestMethod]
    public void LibraryToolbar_UsesSameControlsWithContextBindings()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "Text=\"{Binding ToolbarContextLabel}\"");
        StringAssert.Contains(markup, "Text=\"{Binding ToolbarItemCount, Mode=OneWay}\"");
        StringAssert.Contains(markup, "Text=\"{Binding ToolbarGuidance}\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"영상 검색\"");
        StringAssert.Contains(markup, "x:Name=\"FilterComboBox\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"정렬\"");
    }

    [TestMethod]
    public void LibraryToolbar_FilterSitsBetweenSearchAndSortWithAccessibleCounts()
    {
        var markup = ReadMainWindowMarkup();
        var search = markup.IndexOf("x:Name=\"SearchBox\"", StringComparison.Ordinal);
        var filterLabel = markup.IndexOf("<TextBlock Text=\"필터\"", search, StringComparison.Ordinal);
        var filter = markup.IndexOf("x:Name=\"FilterComboBox\"", StringComparison.Ordinal);
        var sort = markup.IndexOf("AutomationProperties.Name=\"정렬\"", StringComparison.Ordinal);

        Assert.IsTrue(search >= 0 && filterLabel > search && filter > filterLabel && sort > filter);
        StringAssert.Contains(markup[filterLabel..filter], "Margin=\"0,0,10,0\"");
        StringAssert.Contains(markup[filterLabel..filter], "Foreground=\"{StaticResource MutedBrush}\"");
        StringAssert.Contains(markup, "ItemsSource=\"{Binding FilterOptions}\"");
        StringAssert.Contains(markup, "SelectedItem=\"{Binding SelectedFilter}\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"{Binding FilterAutomationName}\"");
        StringAssert.Contains(markup, "Text=\"{Binding Count, StringFormat={}{0}편}\"");
        StringAssert.Contains(markup, "Text=\"✓\"");
        StringAssert.Contains(markup, "Property=\"AutomationProperties.Name\"");
        StringAssert.Contains(markup, "Value=\"{Binding AutomationName}\"");
    }

    [TestMethod]
    public void FilterDropDown_UsesOneBoundedScrollableListAndConditionalGenreHeader()
    {
        var markup = ReadMainWindowMarkup();
        var theme = ReadThemeMarkup();

        StringAssert.Contains(markup, "BasedOn=\"{StaticResource ReferenceSortComboBoxStyle}\"");
        StringAssert.Contains(markup, "ScrollViewer.VerticalScrollBarVisibility=\"Auto\"");
        StringAssert.Contains(markup, "Binding=\"{Binding StartsGenreSection}\"");
        StringAssert.Contains(markup, "Text=\"장르\"");
        var styleStart = theme.IndexOf(
            "x:Key=\"ReferenceSortComboBoxStyle\"",
            StringComparison.Ordinal);
        var styleEnd = theme.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        var style = theme[styleStart..styleEnd];
        StringAssert.Contains(style, "MaxHeight=\"300\"");
        StringAssert.Contains(style, "<ScrollViewer>");
        Assert.AreEqual(
            1,
            style.Split("<ItemsPresenter />", StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    public void FilterControl_ReusesExistingBrushesAndNativeComboBoxBehavior()
    {
        var markup = ReadMainWindowMarkup();
        var code = ReadMainWindowCode();
        var filter = markup.IndexOf("x:Name=\"FilterComboBox\"", StringComparison.Ordinal);
        var styleStart = markup.IndexOf("<ComboBox.Style>", filter, StringComparison.Ordinal);
        var styleEnd = markup.IndexOf("</ComboBox.Style>", styleStart, StringComparison.Ordinal);
        var hoverStart = markup.IndexOf(
            "<Trigger Property=\"IsMouseOver\" Value=\"True\">",
            styleStart,
            StringComparison.Ordinal);
        var hoverEnd = markup.IndexOf("</Trigger>", hoverStart, StringComparison.Ordinal);

        StringAssert.Contains(markup, "Value=\"{StaticResource RaisedBrush}\"");
        StringAssert.Contains(markup, "Value=\"{StaticResource AccentBrush}\"");
        Assert.IsTrue(hoverStart > styleStart && hoverEnd < styleEnd);
        StringAssert.Contains(
            markup[hoverStart..hoverEnd],
            "<Setter Property=\"Foreground\" Value=\"{StaticResource TextBrush}\" />");
        StringAssert.Contains(markup, "x:Name=\"FilterItemBorder\"");
        StringAssert.Contains(code, "ReferenceEquals(e.OriginalSource, FilterComboBox)");
        StringAssert.Contains(code, "e.Key is Key.Enter or Key.Space");
        StringAssert.Contains(code, "e.Key is Key.Enter or Key.Escape");
    }

    [TestMethod]
    public void FilterEmptyState_BindsOnlyFilterSpecificMessages()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "x:Name=\"FilterEmptyState\"");
        StringAssert.Contains(markup, "Visibility=\"{Binding IsFilterEmptyStateVisible, Converter={StaticResource BoolToVisibility}}\"");
        StringAssert.Contains(markup, "Text=\"{Binding FilterEmptyTitle}\"");
        StringAssert.Contains(markup, "Text=\"{Binding FilterEmptyGuidance}\"");
    }

    [TestMethod]
    public void LibraryLoadState_UsesProgressUntilSuccessfulEmptyScan()
    {
        var document = XDocument.Parse(ReadMainWindowMarkup());
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var loading = document.Descendants().Single(element =>
            (string?)element.Attribute(x + "Name") == "LibraryLoadingState");
        var progress = loading.Descendants().Single(element =>
            element.Name.LocalName == "ProgressBar");
        var loadingText = loading.Descendants().Single(element =>
            (string?)element.Attribute("Text") == "영상 목록을 불러오는 중입니다");
        var loadingConditions = loading.Descendants()
            .Where(element => element.Name.LocalName == "Condition")
            .ToDictionary(
                element => (string)element.Attribute("Binding")!,
                element => (string)element.Attribute("Value")!);
        var empty = document.Descendants().Single(element =>
            (string?)element.Attribute(x + "Name") == "NoSupportedVideosState");
        var emptyConditions = empty.Descendants()
            .Where(element => element.Name.LocalName == "Condition")
            .ToDictionary(
                element => (string)element.Attribute("Binding")!,
                element => (string)element.Attribute("Value")!);

        Assert.AreEqual("True", (string?)progress.Attribute("IsIndeterminate"));
        Assert.IsNotNull(loadingText);
        Assert.AreEqual("True", loadingConditions["{Binding IsLibraryLoading}"]);
        Assert.AreEqual("{x:Null}", loadingConditions["{Binding HeroVideo}"]);
        Assert.AreEqual("True", emptyConditions["{Binding HasCompletedLibraryScan}"]);
        Assert.AreEqual("False", emptyConditions["{Binding IsLibraryLoading}"]);
        Assert.AreEqual("0", emptyConditions["{Binding Videos.Count}"]);
    }

    [DataTestMethod]
    [DataRow(30d, 200d, 0d)]
    [DataRow(18d, 200d, 0d)]
    [DataRow(17d, 200d, 1d)]
    [DataRow(-22d, 200d, 40d)]
    [DataRow(-22d, 0d, 0d)]
    public void LibraryToolbarTranslation_UsesBaselineAndRequiresScrolling(
        double originalTop,
        double scrollableHeight,
        double expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.GetLibraryToolbarTranslation(originalTop, scrollableHeight));
    }

    [TestMethod]
    public void LibraryToolbar_UsesSingleRenderTransformForStickyPlacement()
    {
        var document = XDocument.Parse(ReadMainWindowMarkup());
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var scroller = document
            .Descendants(presentation + "ScrollViewer")
            .Single(element =>
                (string?)element.Attribute(x + "Name") == "MainScrollViewer");
        var content = scroller
            .Elements(presentation + "Grid")
            .Single(element =>
                (string?)element.Attribute(x + "Name") == "MainScrollContent");
        var toolbars = content
            .Descendants(presentation + "Border")
            .Where(element =>
                (string?)element.Attribute(x + "Name") == "LibraryToolbar")
            .ToArray();

        Assert.AreEqual(1, toolbars.Length);
        Assert.AreEqual(
            "OnMainScrollChanged",
            (string?)scroller.Attribute("ScrollChanged"));
        Assert.AreEqual(
            "OnMainScrollContentLayoutUpdated",
            (string?)content.Attribute("LayoutUpdated"));
        Assert.AreEqual("1", (string?)toolbars[0].Attribute("Panel.ZIndex"));

        var transform = toolbars[0]
            .Element(presentation + "Border.RenderTransform")?
            .Element(presentation + "TranslateTransform");
        Assert.IsNotNull(transform);
        Assert.AreEqual(
            "LibraryToolbarTransform",
            (string?)transform.Attribute(x + "Name"));
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
    public void CardPopup_RatingsUseDecorativeImageBrushesAndExcludeSeasons()
    {
        var markup = ReadMainWindowMarkup();
        var popupStart = markup.IndexOf(
            "<Popup x:Name=\"CardPopup\"",
            StringComparison.Ordinal);
        var popupEnd = markup.IndexOf("</Popup>", popupStart, StringComparison.Ordinal);
        var popup = markup[popupStart..popupEnd];
        var videoTemplate = CardTemplate(
            popup,
            "<DataTemplate DataType=\"{x:Type main:VideoItemViewModel}\"",
            "<DataTemplate DataType=\"{x:Type main:SeasonItemViewModel}\"");
        var seasonTemplate = CardTemplate(
            popup,
            "<DataTemplate DataType=\"{x:Type main:SeasonItemViewModel}\"",
            "</ContentControl.Resources>");
        var videoRow = videoTemplate.IndexOf("Text=\"영상\"", StringComparison.Ordinal);
        var ratingsRow = videoTemplate.IndexOf("Text=\"평점\"", StringComparison.Ordinal);
        var synopsisRow = videoTemplate.IndexOf("Text=\"시놉시스\"", StringComparison.Ordinal);
        var ratingsBorderStart = videoTemplate.LastIndexOf(
            "<Border",
            ratingsRow,
            StringComparison.Ordinal);
        var ratingsBorder = videoTemplate[ratingsBorderStart..ratingsRow];
        var project = File.ReadAllText(Path.Combine(ProjectDirectory(), "Dabom.csproj"));

        Assert.IsTrue(videoRow >= 0 && ratingsRow > videoRow && synopsisRow > ratingsRow);
        StringAssert.Contains(
            videoTemplate,
            "Visibility=\"{Binding HasRatings, Converter={StaticResource BoolToVisibility}}\"");
        StringAssert.Contains(
            videoTemplate,
            "Visibility=\"{Binding HasImdbRating, Converter={StaticResource BoolToVisibility}}\"");
        StringAssert.Contains(
            videoTemplate,
            "Visibility=\"{Binding HasRottenTomatoesRating, Converter={StaticResource BoolToVisibility}}\"");
        Assert.IsTrue(
            videoTemplate.IndexOf("Assets/imdb.png", StringComparison.Ordinal)
            < videoTemplate.IndexOf("Assets/rotten_tomato.png", StringComparison.Ordinal));
        StringAssert.Contains(
            videoTemplate.ReplaceLineEndings("\n"),
            "Text=\"평점\" FontSize=\"10\"\n                                           AutomationProperties.Name=\"{Binding RatingsAutomationName}\"");
        Assert.IsFalse(ratingsBorder.Contains(
            "AutomationProperties.Name",
            StringComparison.Ordinal));
        Assert.IsFalse(videoTemplate.Contains(
            "<Image Source=\"Assets/imdb.png\"", StringComparison.Ordinal));
        Assert.IsFalse(videoTemplate.Contains(
            "<Image Source=\"Assets/rotten_tomato.png\"", StringComparison.Ordinal));
        StringAssert.Contains(videoTemplate, "<ImageBrush ImageSource=\"Assets/imdb.png\"");
        StringAssert.Contains(videoTemplate, "<ImageBrush ImageSource=\"Assets/rotten_tomato.png\"");
        Assert.IsFalse(seasonTemplate.Contains("HasRatings", StringComparison.Ordinal));
        Assert.IsFalse(seasonTemplate.Contains("HasImdbRating", StringComparison.Ordinal));
        Assert.IsFalse(seasonTemplate.Contains("HasRottenTomatoesRating", StringComparison.Ordinal));
        Assert.IsFalse(seasonTemplate.Contains("Assets/imdb.png", StringComparison.Ordinal));
        Assert.IsFalse(seasonTemplate.Contains("Assets/rotten_tomato.png", StringComparison.Ordinal));
        StringAssert.Contains(
            project,
            "<Resource Include=\"..\\..\\assets\\imdb.png\" Link=\"Assets\\imdb.png\" />");
        StringAssert.Contains(
            project,
            "<Resource Include=\"..\\..\\assets\\rotten_tomato.png\" Link=\"Assets\\rotten_tomato.png\" />");
    }

    [TestMethod]
    public void CardPopup_UsesVideoAndSeasonTemplatesAndAllowsSeasonHover()
    {
        var markup = ReadMainWindowMarkup();
        var code = ReadMainWindowCode();
        var popupStart = markup.IndexOf(
            "<Popup x:Name=\"CardPopup\"",
            StringComparison.Ordinal);
        var popupEnd = markup.IndexOf("</Popup>", popupStart, StringComparison.Ordinal);
        var popup = markup[popupStart..popupEnd];
        var enterStart = code.IndexOf(
            "private void OnCardEnter",
            StringComparison.Ordinal);
        var enterEnd = code.IndexOf(
            "private void OnCardMove",
            enterStart,
            StringComparison.Ordinal);
        var enter = code[enterStart..enterEnd];

        StringAssert.Contains(markup, "x:Name=\"SeasonTypeRibbon\"");
        StringAssert.Contains(popup, "DataType=\"{x:Type main:VideoItemViewModel}\"");
        StringAssert.Contains(popup, "DataType=\"{x:Type main:SeasonItemViewModel}\"");
        StringAssert.Contains(popup, "Text=\"{Binding TotalSummary}\"");
        StringAssert.Contains(popup, "Text=\"{Binding IntroHeading}\"");
        Assert.IsFalse(enter.Contains(
            "VideoItemViewModel",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void CardPopup_DisplaysActualFileNameUnderTitle()
    {
        var fileName = XDocument.Parse(ReadMainWindowMarkup())
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "CardPopupFileName"));

        Assert.AreEqual("{Binding FileName}", (string?)fileName.Attribute("Text"));
        Assert.AreEqual("10", (string?)fileName.Attribute("FontSize"));
        Assert.AreEqual(
            "CharacterEllipsis",
            (string?)fileName.Attribute("TextTrimming"));
    }

    [TestMethod]
    public void MainWindow_OffersAccessibleAboutButtonNextToTitle()
    {
        var markup = ReadMainWindowMarkup();
        var title = markup.IndexOf("Text=\"DABOM\"", StringComparison.Ordinal);
        var about = markup.IndexOf(
            "Content=\"ABOUT\"", title, StringComparison.Ordinal);

        Assert.IsTrue(title >= 0 && about > title);
        StringAssert.Contains(markup, "AutomationProperties.Name=\"DABOM 정보\"");
        StringAssert.Contains(markup, "Click=\"OnAbout\"");
    }

    [TestMethod]
    public void CardPopup_ShowsGenresAndScanningStatus()
    {
        var markup = ReadMainWindowMarkup();

        StringAssert.Contains(markup, "Text=\"장르\"");
        StringAssert.Contains(markup, "Text=\"{Binding GenresText}\"");
        StringAssert.Contains(markup, "Text=\"{Binding StatusMessage}\"");
        Assert.IsFalse(markup.Contains(
            "<DataTrigger Binding=\"{Binding IsScanning}\" Value=\"True\">",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void AboutWindow_ContainsRequiredTmdbAttributionAndKeyboardClose()
    {
        var markup = ReadAboutWindowMarkup();
        var code = ReadAboutWindowCode();

        StringAssert.Contains(markup, "Text=\"DABOM\"");
        StringAssert.Contains(
            markup,
            "지정한 보관 위치의 영화, 드라마, 애니메이션을 찾아 메타데이터와 포스터를 함께 관리하는 Windows 데스크톱 앱입니다.");
        StringAssert.Contains(
            markup,
            "This product uses the TMDB API but is not endorsed or certified by TMDB.");
        StringAssert.Contains(markup, "NavigateUri=\"https://www.themoviedb.org/\"");
        StringAssert.Contains(markup, "IsCancel=\"True\"");
        StringAssert.Contains(markup, "Content=\"닫기\"");
        StringAssert.Contains(
            code,
            "Assembly.GetExecutingAssembly().GetName().Version");
    }

    [TestMethod]
    public void AboutWindow_UsesSmallValidTmdbPng()
    {
        var projectDirectory = ProjectDirectory();
        var logoPath = Path.Combine(
            projectDirectory, "Assets", "TmdbLogo.png");

        Assert.IsTrue(File.Exists(logoPath), "공식 TMDB PNG가 필요합니다.");
        using var stream = File.OpenRead(logoPath);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        Assert.AreEqual(1, decoder.Frames.Count);
        StringAssert.Contains(
            ReadAboutWindowMarkup(),
            "Source=\"Assets/TmdbLogo.png\" Width=\"96\" Height=\"36\"");
        Assert.IsTrue(96 < 256 && 36 < 256, "로고 표시 크기는 앱 아이콘보다 작아야 합니다.");
    }

    [TestMethod]
    public void CardPopup_FollowsPointer()
    {
        var markup = ReadMainWindowMarkup();
        var code = ReadMainWindowCode();

        StringAssert.Contains(markup, "<EventSetter Event=\"MouseMove\" Handler=\"OnCardMove\" />");
        StringAssert.Contains(code, "private void OnCardMove");
        StringAssert.Contains(code, "CardPopup.Placement = PlacementMode.Custom;");
        StringAssert.Contains(code, "CardPopup.PlacementRectangle = new Rect(");
    }

    [STATestMethod]
    [DoNotParallelize]
    public void CardPopup_DoesNotOpenForKeyboardFocus()
    {
        EnsureApplicationResources();
        var window = new MainWindow();
        try
        {
            var videoList = (ListBox)window.FindName("VideoList");
            var card = new ListBoxItem
            {
                DataContext = new object(),
                Style = videoList.ItemContainerStyle,
            };
            var popup = (Popup)window.FindName("CardPopup");
            var focus = new KeyboardFocusChangedEventArgs(
                Keyboard.PrimaryDevice,
                Environment.TickCount,
                null,
                card)
            {
                RoutedEvent = Keyboard.GotKeyboardFocusEvent,
            };

            card.RaiseEvent(focus);

            Assert.IsNull(
                popup.DataContext,
                "툴팁은 키보드 포커스로 대상을 선택하지 않아야 합니다.");
        }
        finally
        {
            window.Close();
        }
    }

    [TestMethod]
    public void MainWindow_WiresSeasonEntryReturnAndVideoOnlyActions()
    {
        var markup = ReadMainWindowMarkup();
        var code = ReadMainWindowCode();
        var click = MethodBody(
            code,
            "private void OnCardClick",
            "private void OpenSeason");

        StringAssert.Contains(markup, "Event=\"PreviewMouseLeftButtonUp\"");
        StringAssert.Contains(markup, "Handler=\"OnCardClick\"");
        StringAssert.Contains(markup, "Click=\"OnReturnToLibrary\"");
        StringAssert.Contains(
            click,
            "Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)");
        StringAssert.Contains(click, "OpenSeason(season);");
        StringAssert.Contains(code, "item.DataContext is SeasonItemViewModel season");
        StringAssert.Contains(code, "IsContinuationOfSeasonEntryClick(");
        StringAssert.Contains(code, "viewModel.SelectedItem is SeasonItemViewModel season");
        StringAssert.Contains(code, "item.DataContext is not VideoItemViewModel video");
        StringAssert.Contains(
            code,
            "e.Key == Key.Escape && viewModel.IsSeasonView");
        StringAssert.Contains(code, "viewModel.CloseSeason();");
        StringAssert.Contains(code, "viewModel.NotifyMissingSelection();");
    }

    [STATestMethod]
    [DoNotParallelize]
    public void SelectionCriteria_KeepSameLogicalFilterAndResetActualChanges()
    {
        EnsureApplicationResources();
        var root = Directory.CreateTempSubdirectory("dabom-multi-selection-");
        var store = new LibraryStore(root.FullName);
        var viewModel = new MainViewModel(
            store,
            new EmptyScanner(),
            new LibraryData(),
            _ => true,
            () => DateTimeOffset.UtcNow,
            _ => 0);
        viewModel.Videos.Add(new VideoItemViewModel(
            Path.Combine(root.FullName, "Movie.A.mkv"),
            new VideoRecord(),
            store));
        viewModel.Videos.Add(new VideoItemViewModel(
            Path.Combine(root.FullName, "Movie.B.mkv"),
            new VideoRecord(),
            store));
        viewModel.SearchText = "Movie";
        var window = new MainWindow { DataContext = viewModel };
        window.Dispatcher.Invoke(
            System.Windows.Threading.DispatcherPriority.DataBind,
            new Action(() => { }));

        try
        {
            var list = (ListBox)window.FindName("VideoList");
            Assert.AreEqual(2, list.Items.Count);

            void SelectBoth()
            {
                foreach (var item in list.Items) list.SelectedItems.Add(item);
                Assert.AreEqual(2, list.SelectedItems.Count);
            }

            SelectBoth();
            Assert.AreEqual(2, viewModel.SelectedItemCount);
            window.ResetSelectionWhenCriteriaChanged(viewModel);
            Assert.AreEqual(2, list.SelectedItems.Count,
                "같은 논리 필터의 재알림은 선택을 유지해야 합니다.");
            Assert.AreEqual(2, viewModel.SelectedItemCount);

            viewModel.SearchText = "MOVIE";
            Assert.AreEqual(0, list.SelectedItems.Count);
            Assert.AreEqual(0, viewModel.SelectedItemCount);

            SelectBoth();
            viewModel.SelectedFilter = viewModel.FilterOptions.Single(option =>
                option.Kind == LibraryFilterKind.MissingMetadata);
            Assert.AreEqual(0, list.SelectedItems.Count);

            SelectBoth();
            viewModel.SelectedSort = VideoSort.ReleaseDate;
            Assert.AreEqual(0, list.SelectedItems.Count);

            SelectBoth();
            viewModel.IsSortDescending = true;
            Assert.AreEqual(0, list.SelectedItems.Count);
        }
        finally
        {
            window.Close();
            root.Delete(true);
        }
    }

    [STATestMethod]
    [DoNotParallelize]
    public void SeasonNavigation_ResetsEntryScrollAndRestoresLibraryOffset()
    {
        EnsureApplicationResources();
        var root = Directory.CreateTempSubdirectory("dabom-season-scroll-");
        var store = new LibraryStore(root.FullName);
        var viewModel = new MainViewModel(
            store,
            new LibraryScanner(),
            new LibraryData(),
            _ => true,
            () => DateTimeOffset.UtcNow,
            _ => 0);
        viewModel.Videos.Add(new VideoItemViewModel(
            Path.Combine(root.FullName, "Episode1.mkv"),
            TvEpisode("에피소드 1", 1),
            store));
        viewModel.Videos.Add(new VideoItemViewModel(
            Path.Combine(root.FullName, "Episode2.mkv"),
            TvEpisode("에피소드 2", 2),
            store));
        viewModel.SearchText = "목록 갱신";
        viewModel.SearchText = string.Empty;
        var season = viewModel.VisibleItems.OfType<SeasonItemViewModel>().Single();
        var window = new MainWindow { DataContext = viewModel };
        System.Windows.Interop.HwndSource? host = null;
        void Pump() => window.Dispatcher.Invoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => { }));

        try
        {
            var rootVisual = (FrameworkElement)window.Content;
            window.Content = null;
            rootVisual.DataContext = viewModel;
            host = new System.Windows.Interop.HwndSource(
                new System.Windows.Interop.HwndSourceParameters
                {
                    WindowName = "DabomSeasonScrollTest",
                    WindowStyle = 0,
                    ExtendedWindowStyle = 0x08000000, // WS_EX_NOACTIVATE
                    Width = 800,
                    Height = 320,
                    PositionX = -32_000,
                    PositionY = -32_000
                })
            {
                RootVisual = rootVisual
            };
            var scroller = (ScrollViewer)window.FindName("MainScrollViewer");
            var content = (FrameworkElement)window.FindName("MainScrollContent");
            content.Height = 2_000;
            Pump();
            Assert.IsTrue(
                scroller.ScrollableHeight > 0,
                $"Extent={scroller.ExtentHeight}, Viewport={scroller.ViewportHeight}, Content={content.ActualHeight}");
            var videoList = (ListBox)window.FindName("VideoList");
            var seasonCard = (ListBoxItem?)videoList.ItemContainerGenerator
                .ContainerFromItem(season);
            Assert.IsNotNull(seasonCard);
            seasonCard.BringIntoView();
            Pump();
            var returnOffset = scroller.VerticalOffset;
            Assert.IsTrue(returnOffset > 0);

            typeof(MainWindow)
                .GetMethod(
                    "OpenSeason",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, [season]);
            Pump();

            Assert.AreEqual(0, scroller.VerticalOffset, 0.1);

            viewModel.CloseSeason();
            Pump();

            Assert.AreEqual(returnOffset, scroller.VerticalOffset, 0.1);
        }
        finally
        {
            host?.Dispose();
            window.Close();
            root.Delete(true);
        }
    }

    [DataTestMethod]
    [DataRow(-20d, 100d, 600d, true)]
    [DataRow(500d, 150d, 600d, true)]
    [DataRow(-120d, 100d, 600d, false)]
    [DataRow(600d, 100d, 600d, false)]
    public void SeasonReturn_RecognizesViewportIntersection(
        double top,
        double height,
        double viewportHeight,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            MainWindow.IntersectsViewport(top, height, viewportHeight));
    }

    [TestMethod]
    public void SeasonEntryDoubleClick_DetectsOnlyTheSamePointerGesture()
    {
        var entry = new Point(40, 50);

        Assert.IsTrue(MainWindow.IsContinuationOfSeasonEntryClick(
            1_000, entry, 1_001, entry));
        Assert.IsFalse(MainWindow.IsContinuationOfSeasonEntryClick(
            1_000,
            entry,
            1_000 + MainWindow.DoubleClickTime + 1,
            entry));
        Assert.IsFalse(MainWindow.IsContinuationOfSeasonEntryClick(
            1_000,
            entry,
            1_001,
            new Point(
                entry.X + MainWindow.DoubleClickWidth + 1,
                entry.Y)));
    }

    [TestMethod]
    public void CardPopupPlacement_KeepsPointerOffsetAndUsesScreenEdgeFallbacks()
    {
        var placements = MainWindow.GetCardPopupPlacements(new Size(430, 600));
        var points = placements.Select(placement => placement.Point).ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                new Point(24, -76),
                new Point(24, -608),
                new Point(-454, -76),
                new Point(-454, -608),
            },
            points);
    }

    [TestMethod]
    public void CardPopup_DoesNotRenderOuterShadow()
    {
        var document = XDocument.Parse(ReadMainWindowMarkup());
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var popup = document
            .Descendants(presentation + "Popup")
            .Single(element =>
                (string?)element.Attribute(x + "Name") == "CardPopup");

        Assert.IsFalse(popup.Descendants(presentation + "DropShadowEffect").Any());
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

    [TestMethod]
    public void ToastStack_IsBottomRightAndPolitelyAnnounced()
    {
        var document = XDocument.Parse(ReadMainWindowMarkup());
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var host = document
            .Descendants(presentation + "StackPanel")
            .SingleOrDefault(element =>
                (string?)element.Attribute(x + "Name") == "ToastHost");
        var announcement = document
            .Descendants(presentation + "TextBlock")
            .SingleOrDefault(element =>
                (string?)element.Attribute(x + "Name") == "ToastAnnouncement");
        Assert.IsNotNull(host, "토스트 스택 호스트가 필요합니다.");
        Assert.IsNotNull(announcement, "화면 읽기 프로그램용 live region이 필요합니다.");
        var liveSetting = announcement.Attributes().SingleOrDefault(attribute =>
            attribute.Name.LocalName == "AutomationProperties.LiveSetting");

        Assert.IsNotNull(liveSetting, "토스트 알림은 live region이어야 합니다.");
        Assert.AreEqual("Right", (string?)host.Attribute("HorizontalAlignment"));
        Assert.AreEqual("Bottom", (string?)host.Attribute("VerticalAlignment"));
        Assert.AreEqual("360", (string?)host.Attribute("Width"));
        Assert.AreEqual("0,0,32,110", (string?)host.Attribute("Margin"));
        Assert.AreEqual("True", (string?)host.Attribute("IsHitTestVisible"));
        Assert.AreEqual("False", (string?)announcement.Attribute("IsHitTestVisible"));
        Assert.AreEqual("Polite", liveSetting.Value);
        Assert.IsFalse(host.Descendants().Any(), "토스트 항목은 런타임에서 추가합니다.");
    }

    [STATestMethod]
    [DoNotParallelize]
    public void VideoToast_ShowsPosterTitleResultAndFilenameAsClickableCard()
    {
        var root = Directory.CreateTempSubdirectory("dabom-video-toast-");
        EnsureApplicationResources();
        var window = new MainWindow();
        try
        {
            var store = new LibraryStore(root.FullName);
            var video = new VideoItemViewModel(
                Path.Combine(root.FullName, "Movie.File.mkv"),
                new VideoRecord { Title = "Movie" },
                store);
            var viewModel = new MainViewModel(
                store,
                new EmptyScanner(),
                new LibraryData(),
                _ => true,
                () => DateTimeOffset.UtcNow,
                _ => 0);
            viewModel.Videos.Add(video);
            window.DataContext = viewModel;
            var request = new ToastRequest(
                "“Movie”이 추가되었습니다.",
                "추가됨",
                video);
            var handler = typeof(MainWindow).GetMethod(
                "OnToastRequested",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            handler.Invoke(window, [null, request]);

            var host = (StackPanel)window.FindName("ToastHost");
            var button = host.Children.OfType<Button>().Single();
            Assert.AreSame(window.FindResource("RaisedBrush"), button.Background);
            Assert.AreSame(window.FindResource("AccentBrush"), button.BorderBrush);
            Assert.IsTrue(button.IsHitTestVisible);
            Assert.AreEqual(
                "“Movie”이 추가되었습니다. 목록에서 보기",
                AutomationProperties.GetName(button));
            var content = (Grid)button.Content;
            var poster = content.Children.OfType<Border>().Single();
            Assert.AreEqual(40, poster.Width);
            Assert.AreEqual(60, poster.Height);
            Assert.AreSame(window.FindResource("SurfaceBrush"), poster.Background);
            Assert.IsInstanceOfType<Border>(poster.Child);
            var lines = content.Children.OfType<StackPanel>().Single();
            var title = (TextBlock)lines.Children[0];
            var details = (Grid)lines.Children[1];
            Assert.AreEqual(0, details.Children.OfType<Border>().Count());
            var detailTexts = details.Children.OfType<TextBlock>().ToArray();
            var result = detailTexts.Single(text => text.Text == "추가됨");
            var separator = detailTexts.Single(text => text.Text == "·");
            var fileName = detailTexts.Single(text => text.Text == "Movie.File.mkv");
            Assert.AreEqual("Movie", title.Text);
            Assert.AreSame(window.FindResource("ToastSuccessBrush"), result.Foreground);
            Assert.AreEqual(FontWeights.SemiBold, result.FontWeight);
            Assert.AreSame(window.FindResource("MutedBrush"), separator.Foreground);
            Assert.AreSame(window.FindResource("MutedBrush"), fileName.Foreground);
            Assert.AreEqual(TextTrimming.CharacterEllipsis, title.TextTrimming);
            Assert.AreEqual(TextTrimming.CharacterEllipsis, fileName.TextTrimming);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.AreSame(video, viewModel.SelectedVideo);
            Assert.AreEqual(0, host.Children.Count);
        }
        finally
        {
            window.Close();
            root.Delete(true);
        }
    }

    [TestMethod]
    public void ToastStatusBrushKey_DistinguishesSuccessWarningAndErrors()
    {
        Assert.AreEqual("ToastSuccessBrush", MainWindow.GetToastStatusBrushKey("추가됨"));
        Assert.AreEqual("ToastWarningBrush", MainWindow.GetToastStatusBrushKey("파일 상태 변경"));
        Assert.AreEqual("ToastErrorBrush", MainWindow.GetToastStatusBrushKey("파일 없음"));
        Assert.AreEqual("ToastErrorBrush", MainWindow.GetToastStatusBrushKey("저장 실패"));
    }

    [TestMethod]
    public void ToastPump_PreservesFifoDuplicatesAndCapsFiveVisibleItems()
    {
        var code = ReadMainWindowCode();
        StringAssert.Contains(code, "private async Task PumpToastsAsync()");
        StringAssert.Contains(code, "private async Task ShowToastAsync");
        var pump = MethodBody(
            code,
            "private async Task PumpToastsAsync()",
            "private async Task ShowToastAsync");

        StringAssert.Contains(code, "Queue<ToastEntry>");
        StringAssert.Contains(code, "Dictionary<ToastEntry, FrameworkElement>");
        StringAssert.Contains(code, "MaxVisibleToasts = 5");
        StringAssert.Contains(code, "TimeSpan.FromSeconds(5)");
        StringAssert.Contains(code, "TimeSpan.FromMilliseconds(200)");
        StringAssert.Contains(pump, "_toastElements.Count < MaxVisibleToasts");
        StringAssert.Contains(pump, "_pendingToasts.TryDequeue");
        Assert.IsFalse(pump.Contains("Distinct", StringComparison.Ordinal));
        Assert.IsFalse(pump.Contains("GroupBy", StringComparison.Ordinal));
        Assert.IsFalse(code.Contains("PriorityQueue", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ToastLifecycle_UsesAnimationSettingAndLiveRegionWithoutFocusChanges()
    {
        var code = ReadMainWindowCode();
        StringAssert.Contains(code, "private FrameworkElement CreateToastVisual");
        StringAssert.Contains(code, "private void AnnounceToast");
        StringAssert.Contains(code, "protected override void OnClosed");
        var announce = MethodBody(
            code,
            "private void AnnounceToast",
            "protected override void OnClosed");

        StringAssert.Contains(code, "ToastRequested += OnToastRequested");
        StringAssert.Contains(code, "ToastRequested -= OnToastRequested");
        StringAssert.Contains(code, "SystemParameters.ClientAreaAnimation");
        StringAssert.Contains(announce, "AutomationEvents.LiveRegionChanged");
        StringAssert.Contains(code, "_pendingToasts.Clear()");
        StringAssert.Contains(code, "_toastElements.Clear()");
        StringAssert.Contains(code, "ToastHost.Children.Clear()");
        Assert.IsFalse(announce.Contains("Focus(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MetadataWindow_ShowsAccessibleStructuredTvFieldsOnlyForEpisodes()
    {
        var markup = ReadMetadataWindowMarkup();

        StringAssert.Contains(markup, "x:Name=\"TvMetadataFields\"");
        StringAssert.Contains(markup, "Binding IsTvEpisode");
        StringAssert.Contains(markup, "x:Name=\"TvSeriesTitleBox\"");
        StringAssert.Contains(markup, "Text=\"{Binding SeriesTitle");
        StringAssert.Contains(markup, "x:Name=\"TvSeasonNumberBox\"");
        StringAssert.Contains(markup, "x:Name=\"TvEpisodeTitleBox\"");
        StringAssert.Contains(markup, "Text=\"{Binding EpisodeTitle");
        StringAssert.Contains(markup, "x:Name=\"TvEpisodeNumberBox\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"TV 시리즈명\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"TV 시즌 번호\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"TV 에피소드명\"");
        StringAssert.Contains(markup, "AutomationProperties.Name=\"TV 에피소드 번호\"");
        StringAssert.Contains(
            markup,
            "AutomationProperties.HelpText=\"{Binding TvValidationMessage}\"");
        StringAssert.Contains(markup, "AutomationProperties.LiveSetting=\"Assertive\"");
    }

    [TestMethod]
    public void MetadataWindow_SearchResultsUseFloatingPopup()
    {
        var markup = ReadMetadataWindowMarkup();
        var document = XDocument.Parse(markup);
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";
        var popup = document
            .Descendants(presentation + "Popup")
            .Single(element =>
                (string?)element.Attribute(x + "Name")
                == "SearchResultsPopup");

        Assert.AreEqual("Bottom", (string?)popup.Attribute("Placement"));
        Assert.AreEqual("False", (string?)popup.Attribute("StaysOpen"));
        StringAssert.Contains(
            (string?)popup.Attribute("IsOpen"),
            "IsSearchPopupOpen");
        StringAssert.Contains(markup, "x:Name=\"SearchBox\"");
        StringAssert.Contains(markup, "ItemsSource=\"{Binding SearchCandidates}\"");
        StringAssert.Contains(markup, "MaxHeight=\"420\"");
    }

    [TestMethod]
    public void MetadataWindow_SearchPopupShowsCandidateSeasonAndEpisodeLists()
    {
        var markup = ReadMetadataWindowMarkup();

        StringAssert.Contains(markup, "Source=\"{Binding PosterUri}\"");
        StringAssert.Contains(markup, "Text=\"{Binding DisplayTitle}\"");
        StringAssert.Contains(markup, "Text=\"{Binding OriginalTitle}\"");
        StringAssert.Contains(markup, "Text=\"{Binding Year}\"");
        StringAssert.Contains(markup, "Value=\"영화\"");
        StringAssert.Contains(markup, "Value=\"TV\"");
        StringAssert.Contains(markup, "Text=\"{Binding LookupHintText}\"");
        StringAssert.Contains(markup, "x:Name=\"TvSeasonsList\"");
        StringAssert.Contains(markup, "ItemsSource=\"{Binding TvSeasons}\"");
        StringAssert.Contains(markup, "x:Name=\"TvEpisodesList\"");
        StringAssert.Contains(markup, "ItemsSource=\"{Binding TvEpisodes}\"");
        StringAssert.Contains(markup, "Content=\"← 이전\"");
        Assert.IsFalse(markup.Contains("x:Name=\"SeasonNumberBox\""));
        Assert.IsFalse(markup.Contains("x:Name=\"EpisodeNumberBox\""));
        Assert.IsFalse(markup.Contains("Content=\"회차 적용\""));
    }

    [TestMethod]
    public void MetadataWindow_WiresSearchKeyboardAndFocusTransitions()
    {
        var markup = ReadMetadataWindowMarkup();
        var code = ReadMetadataWindowCode();

        StringAssert.Contains(markup, "KeyDown=\"OnSearchKeyDown\"");
        StringAssert.Contains(markup, "KeyDown=\"OnSearchResultsKeyDown\"");
        StringAssert.Contains(markup, "PreviewMouseLeftButtonUp=\"OnSeasonClick\"");
        StringAssert.Contains(markup, "KeyDown=\"OnSeasonsKeyDown\"");
        StringAssert.Contains(markup, "PreviewMouseLeftButtonUp=\"OnEpisodeClick\"");
        StringAssert.Contains(markup, "KeyDown=\"OnEpisodesKeyDown\"");
        StringAssert.Contains(markup, "Click=\"OnLookupBack\"");
        StringAssert.Contains(markup, "PreviewKeyDown=\"OnPopupKeyDown\"");
        StringAssert.Contains(code, "list.SelectedIndex = 0;");
        StringAssert.Contains(code, "list.Focus();");
        StringAssert.Contains(
            code,
            "if (viewModel.SearchCandidates.Count == 0)");
        StringAssert.Contains(code, "SearchBox.Focus();");
        StringAssert.Contains(code, "SelectSeasonAsync(season)");
        StringAssert.Contains(code, "SelectEpisodeAsync(episode)");
        StringAssert.Contains(code, "viewModel.GoBackInLookup();");
        StringAssert.Contains(code, "TitleBox.Focus();");
        StringAssert.Contains(code, "e.Key == Key.Escape");
        StringAssert.Contains(code, "viewModel.IsSearchPopupOpen = false;");
        StringAssert.Contains(code, "viewModel.CancelLookup();");
    }

    private static string ReadMainWindowMarkup()
    {
        var path = Path.Combine(ProjectDirectory(), "MainWindow.xaml");
        return File.ReadAllText(path);
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        if (application.TryFindResource("PageBrush") is not null) return;

        application.Resources.MergedDictionaries.Add(
            (ResourceDictionary)Application.LoadComponent(
                new Uri(
                    "/Dabom;component/Styles/DabomTheme.xaml",
                    UriKind.Relative)));
    }

    private static VideoRecord TvEpisode(string title, int episodeNumber) => new()
    {
        Title = title,
        MediaType = MediaType.TvEpisode,
        SeriesTitle = "시리즈",
        SeasonNumber = 1,
        EpisodeNumber = episodeNumber,
        FileSizeBytes = 1,
        LastWriteTimeUtc = DateTimeOffset.UnixEpoch
    };

    private static VideoDeletionRequest DeletionRequest(
        LibraryStore store,
        string path,
        VideoFileStatus status) => new(
        new VideoItemViewModel(path, new VideoRecord(), store),
        status,
        status == VideoFileStatus.Present ? new FileIdentity(7, 10, 20) : null);

    private static string ReadAboutWindowMarkup() =>
        File.ReadAllText(Path.Combine(ProjectDirectory(), "AboutWindow.xaml"));

    private static string ReadVideoDeletionConfirmationMarkup() =>
        File.ReadAllText(Path.Combine(
            ProjectDirectory(),
            "VideoDeletionConfirmationWindow.xaml"));

    private static string ReadAboutWindowCode() =>
        File.ReadAllText(Path.Combine(ProjectDirectory(), "AboutWindow.xaml.cs"));

    private static string ReadMetadataWindowMarkup() =>
        File.ReadAllText(Path.Combine(
            ProjectDirectory(),
            "MetadataWindow.xaml"));

    private static string ReadMetadataWindowCode() =>
        File.ReadAllText(Path.Combine(
            ProjectDirectory(),
            "MetadataWindow.xaml.cs"));

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
        var path = Path.Combine(ProjectDirectory(), "MainWindow.xaml.cs");
        return File.ReadAllText(path);
    }

    private static string ReadMainViewModelCode() =>
        File.ReadAllText(Path.Combine(ProjectDirectory(), "Main", "MainViewModel.cs"));

    private static string CardTemplate(string markup, string startMarker, string endMarker)
    {
        var start = markup.IndexOf(startMarker, StringComparison.Ordinal);
        var end = markup.IndexOf(endMarker, start, StringComparison.Ordinal);
        return markup[start..end];
    }

    private static string MethodBody(string code, string startMarker, string endMarker)
    {
        var start = code.IndexOf(startMarker, StringComparison.Ordinal);
        var end = code.IndexOf(endMarker, start, StringComparison.Ordinal);
        return code[start..end];
    }

    private static void AssertPosterPrecedesOpaqueMissingRibbon(string template)
    {
        var posterStart = template.IndexOf(
            "<Border Margin=\"1\" CornerRadius=\"15\"",
            StringComparison.Ordinal);
        var posterEnd = template.IndexOf("</Border>", posterStart, StringComparison.Ordinal);
        var ribbonStart = template.IndexOf(
            "<Border VerticalAlignment=\"Bottom\"",
            posterEnd,
            StringComparison.Ordinal);

        StringAssert.Contains(template[posterStart..posterEnd], "Opacity=\"{Binding PosterOpacity}\"");
        StringAssert.Contains(template[ribbonStart..], "Background=\"#FFD63C3C\"");
        StringAssert.Contains(template[ribbonStart..], "Margin=\"1,0,1,1\"");
        StringAssert.Contains(template[ribbonStart..], "CornerRadius=\"0,0,15,15\"");
        Assert.IsFalse(template[posterStart..posterEnd].Contains("#FFD63C3C", StringComparison.Ordinal));
    }

    private sealed class EmptyScanner : ILibraryScanner
    {
        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            IProgress<int>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ScanResult(
                new Dictionary<string, ScannedVideo>(StringComparer.OrdinalIgnoreCase),
                []));
    }

    private static string ProjectDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Dabom"));
}
