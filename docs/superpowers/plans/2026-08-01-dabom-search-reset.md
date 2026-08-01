# DABOM 검색어 초기화 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 메인 라이브러리와 메타데이터 편집 검색란에 검색어가 있을 때만 `×` 버튼을 표시하고, 버튼 클릭 또는 검색란 포커스 상태의 `Esc`로 검색어를 초기화한다.

**Architecture:** 두 창의 기존 `SearchText` 바인딩과 코드 비하인드 이벤트 구조를 그대로 사용한다. 각 XAML에 작은 초기화 버튼을 추가하고, 각 창 안의 단일 `ClearSearch()` helper가 클릭과 `Esc` 경로를 합친다. 메타데이터 창 helper만 결과 팝업을 닫으며 ViewModel, 공용 컨트롤, 테마 파일은 변경하지 않는다.

**Tech Stack:** .NET 9, WPF XAML, C# 코드 비하인드, MSTest

## Global Constraints

- 대상은 메인 화면의 `제목, 감독, 배우 이름으로 검색` 검색창과 메타데이터 편집 창의 온라인 검색란이다.
- 초기화 버튼은 검색어가 있을 때만 표시하고 접근성 이름과 도구 설명을 모두 `검색어 지우기`로 제공한다.
- `Esc`는 검색란에 키보드 포커스가 있고 검색어가 비어 있지 않을 때만 초기화에 사용한다.
- 검색란 밖의 `Esc`, 빈 검색란의 `Esc`, 메인 검색의 `Ctrl+K`, 메타데이터 온라인 검색·결과 선택·저장 동작은 유지한다.
- 메타데이터 초기화는 결과 팝업을 닫되 기존 후보 데이터, 편집값, 오류 메시지 및 저장 상태를 변경하지 않는다.
- 메인 검색의 기존 필터 및 선택 해제 규칙을 변경하지 않는다.
- 공용 검색 컨트롤, 새 ViewModel 명령, 새 의존성, 애니메이션 및 검색 기록 기능을 추가하지 않는다.
- `src/Dabom/Styles/DabomTheme.xaml`은 현재 미커밋 변경이 있으므로 읽기만 하고 수정하거나 스테이징하지 않는다.
- 기존 미커밋 파일과 `.superpowers/` 파일은 되돌리거나 덮어쓰거나 기능 커밋에 포함하지 않는다.
- GUI, 브라우저, 오디오 또는 알림을 띄우지 않고 자동 테스트와 빌드만 실행한다.
- 설계 기준은 `docs/superpowers/specs/2026-08-01-dabom-search-reset-design.md`다.

## File Structure

- Modify: `src/Dabom/MainWindow.xaml` — 메인 검색창의 `Ctrl K` 안내와 교대 표시되는 초기화 버튼을 배치한다.
- Modify: `src/Dabom/MainWindow.xaml.cs` — 메인 검색 초기화 클릭과 검색란 포커스 상태의 `Esc`를 처리한다.
- Modify: `src/Dabom/MetadataWindow.xaml` — 온라인 검색 입력 영역 내부 우측에 초기화 버튼을 겹쳐 배치한다.
- Modify: `src/Dabom/MetadataWindow.xaml.cs` — 메타데이터 검색어와 결과 팝업을 함께 초기화한다.
- Modify: `tests/Dabom.Tests/MainWindowMarkupTests.cs` — 두 창의 표시 조건, 접근성 속성, 이벤트 연결 및 초기화 코드 계약을 검증한다.
- Inspect only: `src/Dabom/Main/MainViewModel.cs`, `src/Dabom/Metadata/MetadataEditorViewModel.cs` — 기존 `SearchText`와 `IsSearchPopupOpen`을 그대로 소비한다.
- Inspect only: `src/Dabom/Styles/DabomTheme.xaml` — 기존 Button 스타일과 `ControlCornerRadius=12`를 재사용한다.
- Update after code changes: `graphify-out/*` — `graphify update .`로 지식 그래프를 최신화한다.

---

### Task 1: 메인 라이브러리 검색 초기화

**Files:**

- Modify: `tests/Dabom.Tests/MainWindowMarkupTests.cs:209-220`
- Modify: `src/Dabom/MainWindow.xaml:332-381`
- Modify: `src/Dabom/MainWindow.xaml.cs:112-143`

**Interfaces:**

- Consumes: `MainViewModel.SearchText`, 기존 `SearchBox`, `OnPreviewKeyDown`
- Produces: `OnClearSearch(object, RoutedEventArgs)`, `ClearSearch()`
- Preserves: `Ctrl+K`, 빈 검색란과 검색란 밖의 기존 `Esc`, 목록 필터 및 선택 해제 규칙

- [ ] **Step 1: 실패하는 메인 검색 UI·키보드 계약 테스트 작성**

`MainWindowMarkupTests.LibraryToolbar_ContainsSearchGuidanceAndSortLabel` 다음에 아래 테스트를 추가한다.

```csharp
[TestMethod]
public void MainSearch_WiresVisibleClearButtonAndEscapeReset()
{
    var document = XDocument.Parse(ReadMainWindowMarkup());
    var code = ReadMainWindowCode();
    XNamespace presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    var clearButton = document
        .Descendants(presentation + "Button")
        .Single(element =>
            (string?)element.Attribute(x + "Name") == "SearchClearButton");
    var emptyTrigger = clearButton
        .Descendants(presentation + "DataTrigger")
        .Single(element => (string?)element.Attribute("Value") == "");
    var shortcutHint = document
        .Descendants(presentation + "Border")
        .Single(element =>
            (string?)element.Attribute(x + "Name") == "SearchShortcutHint");

    Assert.AreEqual("×", (string?)clearButton.Attribute("Content"));
    Assert.AreEqual("OnClearSearch", (string?)clearButton.Attribute("Click"));
    Assert.AreEqual(
        "검색어 지우기",
        (string?)clearButton.Attribute("AutomationProperties.Name"));
    Assert.AreEqual("검색어 지우기", (string?)clearButton.Attribute("ToolTip"));
    Assert.AreEqual(
        "Collapsed",
        (string?)emptyTrigger.Element(presentation + "Setter")?.Attribute("Value"));
    StringAssert.Contains(shortcutHint.ToString(), "Binding SearchText");
    StringAssert.Contains(shortcutHint.ToString(), "Value=\"Visible\"");
    StringAssert.Contains(code, "SearchBox.IsKeyboardFocusWithin");
    StringAssert.Contains(code, "!string.IsNullOrEmpty(viewModel.SearchText)");
    StringAssert.Contains(code, "viewModel.SearchText = string.Empty;");
}
```

- [ ] **Step 2: 대상 테스트가 초기화 버튼 부재로 실패하는지 확인**

Run:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --filter "FullyQualifiedName~MainSearch_WiresVisibleClearButtonAndEscapeReset" --nologo
```

Expected: `SearchClearButton`을 찾지 못해 테스트 1개가 실패한다.

- [ ] **Step 3: 메인 검색창 우측 표시 영역을 `Ctrl K` 안내와 초기화 버튼이 교대하도록 변경**

`MainWindow.xaml`의 기존 `Ctrl K` Border에 `x:Name`과 표시 Style을 추가한다. 검색어가 비어 있거나 `null`일 때만 안내를 표시한다.

```xml
<Border x:Name="SearchShortcutHint" Grid.Column="2"
        Margin="12,0,18,0" Padding="7,4"
        HorizontalAlignment="Right" VerticalAlignment="Center"
        CornerRadius="{StaticResource ControlCornerRadius}"
        Background="{StaticResource RaisedBrush}"
        BorderBrush="{StaticResource LineBrush}" BorderThickness="1">
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding SearchText}" Value="">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
                <DataTrigger Binding="{Binding SearchText}" Value="{x:Null}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <TextBlock Text="Ctrl K" FontSize="10"
               Foreground="{StaticResource MutedBrush}" />
</Border>
```

바로 다음에 같은 Grid 열을 쓰는 24×24 버튼을 추가한다. 기존 전역 Button template의 12 DIP 모서리를 그대로 사용해 원형을 만들고, 검색어가 비면 접는다.

```xml
<Button x:Name="SearchClearButton" Grid.Column="2"
        Width="24" Height="24" Margin="12,0,18,0" Padding="0"
        HorizontalAlignment="Right" VerticalAlignment="Center"
        Content="×" FontSize="15" FontWeight="SemiBold"
        Background="{StaticResource RaisedBrush}"
        AutomationProperties.Name="검색어 지우기"
        ToolTip="검색어 지우기"
        Click="OnClearSearch">
    <Button.Style>
        <Style TargetType="Button"
               BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding SearchText}" Value="">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
                <DataTrigger Binding="{Binding SearchText}" Value="{x:Null}">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>
```

- [ ] **Step 4: 클릭과 검색란 포커스 상태의 `Esc`를 한 helper로 연결**

`MainWindow.xaml.cs`에서 `OnPreviewKeyDown`의 기존 마지막 `Escape` 분기 바로 앞에 아래 분기를 추가한다.

```csharp
else if (e.Key == Key.Escape
    && SearchBox.IsKeyboardFocusWithin
    && !string.IsNullOrEmpty(viewModel.SearchText))
{
    ClearSearch();
    e.Handled = true;
}
```

`OnPreviewKeyDown` 다음에 아래 메서드를 추가한다.

```csharp
private void OnClearSearch(object sender, RoutedEventArgs e) => ClearSearch();

private void ClearSearch()
{
    var viewModel = (MainViewModel)DataContext;
    viewModel.SearchText = string.Empty;
    SearchBox.Focus();
}
```

새 분기 뒤에는 기존 `else if (e.Key == Key.Escape)` 본문을 그대로 둔다. 따라서 검색어가 비었거나 검색란 밖에 포커스가 있으면 기존 팝업 닫기와 포커스 해제가 실행된다.

- [ ] **Step 5: 메인 검색 대상 테스트와 기존 필터 테스트 통과 확인**

Run:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --filter "FullyQualifiedName~MainSearch_WiresVisibleClearButtonAndEscapeReset|FullyQualifiedName~Search_ClearsExcludedSelectionButKeepsIncludedSelection" --nologo
```

Expected: 새 계약 테스트와 기존 검색 필터 테스트가 모두 통과한다.

- [ ] **Step 6: 메인 검색 변경만 커밋**

먼저 diff가 Task 1의 세 파일과 검색 초기화 줄로만 제한되는지 확인한다.

```powershell
git diff -- src/Dabom/MainWindow.xaml src/Dabom/MainWindow.xaml.cs tests/Dabom.Tests/MainWindowMarkupTests.cs
git diff --check
```

문제가 없으면 정확히 세 파일만 스테이징하고 커밋한다.

```powershell
git add src/Dabom/MainWindow.xaml src/Dabom/MainWindow.xaml.cs tests/Dabom.Tests/MainWindowMarkupTests.cs
git diff --cached --check
git commit -m "feat: clear library search input"
```

Expected: 기존 미커밋 파일은 스테이징되지 않고 메인 검색 초기화만 커밋된다.

---

### Task 2: 메타데이터 검색 초기화와 전체 회귀 검증

**Files:**

- Modify: `tests/Dabom.Tests/MainWindowMarkupTests.cs:494-514`
- Modify: `src/Dabom/MetadataWindow.xaml:70-82`
- Modify: `src/Dabom/MetadataWindow.xaml.cs:33-52`
- Update generated output: `graphify-out/*`

**Interfaces:**

- Consumes: `MetadataEditorViewModel.SearchText`, `MetadataEditorViewModel.IsSearchPopupOpen`, 기존 `SearchBox`, `OnSearchKeyDown`
- Produces: `OnClearSearch(object, RoutedEventArgs)`, `ClearSearch()`
- Preserves: 결과 후보, 적용·편집한 메타데이터, 오류 메시지, 온라인 검색, 팝업 내부 `Esc`

- [ ] **Step 1: 실패하는 메타데이터 검색 UI·키보드 계약 테스트 작성**

`MainWindowMarkupTests.MetadataWindow_WiresSearchKeyboardAndFocusTransitions` 다음에 아래 테스트를 추가한다.

```csharp
[TestMethod]
public void MetadataSearch_WiresVisibleClearButtonAndEscapeReset()
{
    var document = XDocument.Parse(ReadMetadataWindowMarkup());
    var code = ReadMetadataWindowCode();
    XNamespace presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace x =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    var clearButton = document
        .Descendants(presentation + "Button")
        .Single(element =>
            (string?)element.Attribute(x + "Name") == "SearchClearButton");
    var emptyTrigger = clearButton
        .Descendants(presentation + "DataTrigger")
        .Single(element => (string?)element.Attribute("Value") == "");

    Assert.AreEqual("×", (string?)clearButton.Attribute("Content"));
    Assert.AreEqual("OnClearSearch", (string?)clearButton.Attribute("Click"));
    Assert.AreEqual(
        "검색어 지우기",
        (string?)clearButton.Attribute("AutomationProperties.Name"));
    Assert.AreEqual("검색어 지우기", (string?)clearButton.Attribute("ToolTip"));
    Assert.AreEqual(
        "Collapsed",
        (string?)emptyTrigger.Element(presentation + "Setter")?.Attribute("Value"));
    StringAssert.Contains(code, "!string.IsNullOrEmpty(viewModel.SearchText)");
    StringAssert.Contains(code, "viewModel.SearchText = string.Empty;");
    StringAssert.Contains(code, "viewModel.IsSearchPopupOpen = false;");
}
```

- [ ] **Step 2: 대상 테스트가 초기화 버튼 부재로 실패하는지 확인**

Run:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --filter "FullyQualifiedName~MetadataSearch_WiresVisibleClearButtonAndEscapeReset" --nologo
```

Expected: 메타데이터 XAML에서 `SearchClearButton`을 찾지 못해 테스트 1개가 실패한다.

- [ ] **Step 3: 메타데이터 검색 입력 영역 안에 초기화 버튼 배치**

`MetadataWindow.xaml`에서 기존 `SearchBox`를 Grid로 감싼다. 온라인 검색 버튼과 Popup은 이동하지 않는다.

```xml
<Grid>
    <TextBox x:Name="SearchBox"
             Padding="9,7,38,7"
             Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
             KeyDown="OnSearchKeyDown" />
    <Button x:Name="SearchClearButton"
            Width="24" Height="24" Margin="0,0,8,0" Padding="0"
            HorizontalAlignment="Right" VerticalAlignment="Center"
            Content="×" FontSize="15" FontWeight="SemiBold"
            Background="{StaticResource RaisedBrush}"
            AutomationProperties.Name="검색어 지우기"
            ToolTip="검색어 지우기"
            Click="OnClearSearch">
        <Button.Style>
            <Style TargetType="Button"
                   BasedOn="{StaticResource {x:Type Button}}">
                <Setter Property="Visibility" Value="Visible" />
                <Style.Triggers>
                    <DataTrigger Binding="{Binding SearchText}" Value="">
                        <Setter Property="Visibility" Value="Collapsed" />
                    </DataTrigger>
                    <DataTrigger Binding="{Binding SearchText}" Value="{x:Null}">
                        <Setter Property="Visibility" Value="Collapsed" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Button.Style>
    </Button>
</Grid>
```

`Padding="9,7,38,7"`은 기존 왼쪽·세로 여백을 유지하면서 입력 텍스트가 24 DIP 버튼과 8 DIP 우측 여백 아래로 들어가지 않게 한다.

- [ ] **Step 4: 검색란 `Esc`와 클릭을 검색어·팝업 초기화 helper로 연결**

`MetadataWindow.xaml.cs.OnSearchKeyDown`의 기존 팝업 닫기 분기보다 먼저 아래 분기를 둔다. 이 handler는 `SearchBox`에만 연결되어 있으므로 별도 포커스 검사는 추가하지 않는다.

```csharp
if (e.Key == Key.Escape && !string.IsNullOrEmpty(viewModel.SearchText))
{
    e.Handled = true;
    ClearSearch();
    return;
}
```

`OnSearchClick` 다음에 아래 메서드를 추가한다.

```csharp
private void OnClearSearch(object sender, RoutedEventArgs e) => ClearSearch();

private void ClearSearch()
{
    var viewModel = (MetadataEditorViewModel)DataContext;
    viewModel.SearchText = string.Empty;
    viewModel.IsSearchPopupOpen = false;
    SearchBox.Focus();
}
```

기존 `OnPopupKeyDown`은 그대로 둔다. 결과 목록에 포커스가 있을 때 `Esc`는 검색어를 지우지 않고 팝업만 닫은 뒤 검색란으로 돌아간다.

- [ ] **Step 5: 메타데이터 대상 테스트와 기존 검색 흐름 통과 확인**

Run:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --filter "FullyQualifiedName~MetadataSearch_WiresVisibleClearButtonAndEscapeReset|FullyQualifiedName~MetadataWindow_WiresSearchKeyboardAndFocusTransitions|FullyQualifiedName~SearchAsync_InitializesQueryAndRejectsBlankInput" --nologo
```

Expected: 새 계약 테스트, 기존 키보드 연결 테스트, 기존 검색 입력 검증 테스트가 모두 통과한다.

- [ ] **Step 6: 전체 자동 회귀와 Release XAML 컴파일 확인**

Run:

```powershell
dotnet test Dabom.sln -c Debug --nologo
dotnet build Dabom.sln -c Release --nologo
git diff --check
```

Expected: 전체 테스트 실패 0개, Release 빌드 오류 0개, 공백 오류 0개다. GUI 창은 열리지 않는다.

- [ ] **Step 7: 메타데이터 검색 변경만 커밋**

```powershell
git diff -- src/Dabom/MetadataWindow.xaml src/Dabom/MetadataWindow.xaml.cs tests/Dabom.Tests/MainWindowMarkupTests.cs
git add src/Dabom/MetadataWindow.xaml src/Dabom/MetadataWindow.xaml.cs tests/Dabom.Tests/MainWindowMarkupTests.cs
git diff --cached --check
git commit -m "feat: clear metadata search input"
```

Expected: Task 2의 메타데이터 검색 초기화와 해당 테스트만 커밋된다.

- [ ] **Step 8: Graphify 갱신과 최종 작업 트리 확인**

Run:

```powershell
graphify update .
git status --short
```

Expected: 지식 그래프가 최신 코드 구조를 반영한다. 구현 전부터 존재한 `docs/superpowers/specs/2026-07-31-dabom-manual-metadata-search-design.md`, `src/Dabom/Styles/DabomTheme.xaml`, `tests/Dabom.Tests/WindowChromeMarkupTests.cs`, `.superpowers/` 변경은 그대로 남고 기능 커밋에 포함되지 않는다.
