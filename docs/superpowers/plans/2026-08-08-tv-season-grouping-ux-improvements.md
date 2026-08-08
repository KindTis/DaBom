# DABOM TV 시즌 그룹 탐색 UX 개선 구현 계획

> **에이전트 작업자 필수:** REQUIRED SUB-SKILL: 이 계획을 작업별로 구현할 때 `superpowers:subagent-driven-development`(권장) 또는 `superpowers:executing-plans`를 사용한다. 진행 추적에는 체크박스(`- [ ]`)를 사용한다.

**목표:** 시즌 그룹 카드를 선명하게 구분하고, 그룹 팝업과 현재 시즌 Hero를 제공하며, 전체 목록과 시즌 상세에서 동일한 검색 툴바 구조를 유지한다.

**아키텍처:** 기존 `SeasonItemViewModel`이 전체 그룹에서 대표 에피소드와 총 편수를 계산하고, `MainViewModel`이 활성 시즌과 현재 Hero 영상을 화면 상태로 노출한다. 기존 Featured 상태, `PlayAsync`, 카드 팝업 위치 계산과 시즌 진입·복귀 흐름을 재사용하고 XAML의 형식별 템플릿만 확장한다.

**기술 스택:** C# 13, .NET 9, WPF/XAML, MSTest 3.6.4

## 전역 제약 사항

- 기준 명세는 `docs/superpowers/specs/2026-08-08-tv-season-grouping-ux-improvements-design.md`다.
- 신규 서비스, 인터페이스, 저장 모델, 네트워크 호출, 패키지와 프로젝트 파일을 추가하지 않는다.
- `FeaturedVideo`, `VideoRecord`, `LibraryData`, 공급자 참조와 JSON 형식을 변경하지 않는다.
- 시즌 소개 정보는 현재 실행의 화면 상태로만 유지하고 저장하지 않는다.
- 검색·필터·정렬, 시즌 그룹 판정, 포커스·스크롤 복원과 자동 그룹 해체의 기존 계약을 유지한다.
- GUI, 브라우저, 오디오와 알림을 띄우지 않는 MSTest·XAML 정적 검사·Debug 빌드만 자동 실행한다.
- 사용자 대상 문서는 한국어 UTF-8(BOM 없음)으로 유지한다.
- 코드 수정 후 `graphify update .`를 실행한다.
- 구현 시작 기준선은 커밋 `21da155`이며 전체 MSTest 237개가 통과한다.

---

## 파일 구조

- 수정: `src/Dabom/Main/SeasonItemViewModel.cs` — 총 편수, 대표 에피소드와 소개 문구를 한 곳에서 계산한다.
- 수정: `src/Dabom/Main/MainViewModel.cs` — 활성 시즌, Hero 영상, 공통 툴바 문맥과 재생 후 재계산을 관리한다.
- 수정: `src/Dabom/MainWindow.xaml` — 시즌 리본, 시즌 Hero, 공통 툴바 바인딩과 형식별 팝업 템플릿을 표시한다.
- 수정: `src/Dabom/MainWindow.xaml.cs` — 기존 hover 팝업을 시즌 카드에도 허용한다.
- 수정: `tests/Dabom.Tests/MainViewModelTests.cs` — 대표 선택, 검색 독립성, 재생 성공·실패 상태를 검증한다.
- 수정: `tests/Dabom.Tests/MainWindowMarkupTests.cs` — 리본, Hero, 공통 툴바, 팝업 템플릿과 hover 연결을 검증한다.
- 새 프로덕션 파일은 만들지 않는다.

---

### 작업 1: 시즌 대표 에피소드 계산

**파일:**

- 수정: `src/Dabom/Main/SeasonItemViewModel.cs:49-72`
- 테스트: `tests/Dabom.Tests/MainViewModelTests.cs:588-640`

**인터페이스:**

- 입력: 기존 `SeasonItemViewModel(SeasonGroupKey, IReadOnlyList<VideoItemViewModel>, IReadOnlyList<VideoItemViewModel>)`
- 출력: `int TotalEpisodeCount`, `string TotalSummary`, `VideoItemViewModel IntroEpisode`, `string IntroLabel`, `string IntroHeading`
- 선택 규칙: 미재생 후보 우선 → 양수 회차 오름차순 → 무효 회차 → 기존 전체 그룹 순서

- [ ] **1단계: 대표 선택 규칙을 고정하는 실패 테스트 작성**

`MainViewModelTests`의 기존 `SeasonItem_UsesMatchedOrderForTextAndWholeGroupForPoster` 다음에 아래 테스트를 추가한다.

```csharp
[TestMethod]
public void SeasonItem_SelectsFirstUnplayedEpisodeAndFallsBackToFirstOverall()
{
    var root = Directory.CreateTempSubdirectory("dabom-season-intro-");
    try
    {
        var store = new LibraryStore(root.FullName);
        var playedAt = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
        VideoItemViewModel Episode(string name, int? number, bool played) => new(
            Path.Combine(root.FullName, $"{name}.mkv"),
            TvRecord(name, "시리즈", 1, number, "10") with
            {
                EpisodeTitle = name,
                LastPlayedUtc = played ? playedAt : null
            },
            store);

        var unknown = Episode("회차 없음", null, false);
        var first = Episode("첫 화", 1, true);
        var second = Episode("두 번째 화", 2, true);
        var third = Episode("세 번째 화", 3, false);
        var fourth = Episode("네 번째 화", 4, false);
        var key = SeasonGroupKey.From(first.Record)!;
        var season = new SeasonItemViewModel(
            key,
            [fourth],
            [unknown, first, second, third, fourth]);

        Assert.AreEqual(5, season.TotalEpisodeCount);
        Assert.AreEqual("시즌 1 · 총 5편", season.TotalSummary);
        Assert.AreSame(third, season.IntroEpisode);
        Assert.AreEqual("다음 미시청 에피소드", season.IntroLabel);
        Assert.AreEqual("3화 · 세 번째 화", season.IntroHeading);

        var allPlayed = new[]
        {
            Episode("첫 화", 1, true),
            Episode("두 번째 화", 2, true),
            Episode("회차 없음", null, true)
        };
        var replay = new SeasonItemViewModel(key, allPlayed, allPlayed);

        Assert.AreSame(allPlayed[0], replay.IntroEpisode);
        Assert.AreEqual("처음부터 보기", replay.IntroLabel);
        Assert.AreEqual("1화 · 첫 화", replay.IntroHeading);
    }
    finally
    {
        root.Delete(true);
    }
}
```

- [ ] **2단계: 신규 테스트가 예상대로 실패하는지 확인**

실행:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~SeasonItem_SelectsFirstUnplayedEpisodeAndFallsBackToFirstOverall"
```

예상: `SeasonItemViewModel`에 `TotalEpisodeCount`, `TotalSummary`, `IntroEpisode`, `IntroLabel`, `IntroHeading`이 없어 컴파일 실패한다.

- [ ] **3단계: `SeasonItemViewModel`에 최소 계산 속성 구현**

기존 포스터 선택과 현재 결과용 `EpisodeCount`를 유지하면서 전체 그룹을 보관하고 아래 속성과 선택 메서드를 추가한다.

```csharp
private readonly IReadOnlyList<VideoItemViewModel> _wholeGroup;

internal SeasonItemViewModel(
    SeasonGroupKey key,
    IReadOnlyList<VideoItemViewModel> episodes,
    IReadOnlyList<VideoItemViewModel> wholeGroup)
{
    Key = key;
    Episodes = episodes;
    _wholeGroup = wholeGroup;
    DisplayTitle = episodes[0].Record.SeriesTitle!.Trim();
    Poster = wholeGroup.FirstOrDefault(video => video.HasPoster)?.Poster;
}

public int TotalEpisodeCount => _wholeGroup.Count;
public string TotalSummary => $"시즌 {SeasonNumber} · 총 {TotalEpisodeCount}편";
public VideoItemViewModel IntroEpisode => SelectIntroEpisode(_wholeGroup);
public string IntroLabel => _wholeGroup.Any(video => video.Record.LastPlayedUtc is null)
    ? "다음 미시청 에피소드"
    : "처음부터 보기";
public string IntroHeading
{
    get
    {
        var episode = IntroEpisode;
        var title = string.IsNullOrWhiteSpace(episode.Record.EpisodeTitle)
            ? episode.DisplayTitle
            : episode.Record.EpisodeTitle;
        return episode.Record.EpisodeNumber is > 0
            ? $"{episode.Record.EpisodeNumber}화 · {title}"
            : title;
    }
}

private static VideoItemViewModel SelectIntroEpisode(
    IReadOnlyList<VideoItemViewModel> wholeGroup)
{
    var unplayed = wholeGroup
        .Where(video => video.Record.LastPlayedUtc is null)
        .ToArray();
    var candidates = unplayed.Length > 0 ? unplayed : wholeGroup;
    return candidates
        .Select((episode, index) => (Episode: episode, Index: index))
        .OrderBy(item => item.Episode.Record.EpisodeNumber is > 0 ? 0 : 1)
        .ThenBy(item => item.Episode.Record.EpisodeNumber is > 0
            ? item.Episode.Record.EpisodeNumber.Value
            : int.MaxValue)
        .ThenBy(item => item.Index)
        .First()
        .Episode;
}
```

`wholeGroup`은 기존 그룹 규칙상 최소 2편이므로 빈 컬렉션 전용 방어 로직을 추가하지 않는다.

- [ ] **4단계: 시즌 모델 테스트 통과 확인**

실행:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~SeasonItem_"
```

예상: 신규 대표 선택 테스트와 기존 시즌 카드 테스트가 모두 통과한다.

- [ ] **5단계: 작업 1 커밋**

```powershell
git add src/Dabom/Main/SeasonItemViewModel.cs tests/Dabom.Tests/MainViewModelTests.cs
git commit -m "feat: select season intro episodes"
```

---

### 작업 2: 활성 시즌 Hero와 공통 툴바 상태

**파일:**

- 수정: `src/Dabom/Main/MainViewModel.cs:78-84,123-157,209-229,478-519,677-766,875-881`
- 테스트: `tests/Dabom.Tests/MainViewModelTests.cs:188-270,1290-1343`

**인터페이스:**

- 소비: 작업 1의 `SeasonItemViewModel.IntroEpisode`, `TotalEpisodeCount`, `IntroLabel`, `IntroHeading`
- 출력: `SeasonItemViewModel? ActiveSeason`, `VideoItemViewModel? HeroVideo`, `string ToolbarContextLabel`, `int ToolbarItemCount`, `string ToolbarGuidance`
- 재사용: 기존 `PlayFeaturedCommand`와 `PlayAsync(VideoItemViewModel)`

- [ ] **1단계: 전체 그룹 Hero와 검색 독립성 실패 테스트 작성**

```csharp
[TestMethod]
public async Task SeasonView_UsesWholeGroupHeroAndContextAcrossSearch()
{
    var root = Directory.CreateTempSubdirectory("dabom-season-hero-");
    try
    {
        var paths = Enumerable.Range(1, 4)
            .Select(number => Path.Combine(root.FullName, $"Episode {number}.mkv"))
            .ToArray();
        var playedAt = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        var data = CachedData(root.FullName, paths);
        for (var index = 0; index < paths.Length; index++)
        {
            data.VideosByPath[paths[index]] = TvRecord(
                $"Episode {index + 1}", "시리즈", 1, index + 1, "10") with
            {
                EpisodeTitle = $"에피소드 {index + 1}",
                LastPlayedUtc = index < 2 ? playedAt : null
            };
        }
        var vm = CreateViewModel(
            new LibraryStore(root.FullName),
            new StubScanner(paths),
            data);
        await vm.ScanAsync();
        var featured = vm.FeaturedVideo;

        Assert.IsTrue(vm.OpenSeason(
            vm.VisibleItems.OfType<SeasonItemViewModel>().Single()));

        Assert.AreEqual(paths[2], vm.HeroVideo!.Path);
        Assert.AreEqual(4, vm.ActiveSeason!.TotalEpisodeCount);
        Assert.AreEqual("에피소드", vm.ToolbarContextLabel);
        Assert.AreEqual(4, vm.ToolbarItemCount);

        vm.SearchText = "Episode 4";

        Assert.AreEqual(1, vm.DisplayItemCount);
        Assert.AreEqual(1, vm.ToolbarItemCount);
        Assert.AreEqual(paths[2], vm.HeroVideo!.Path);

        vm.CloseSeason();

        Assert.AreSame(featured, vm.HeroVideo);
        Assert.AreEqual("내 영상", vm.ToolbarContextLabel);
        Assert.AreEqual(vm.VisibleCount, vm.ToolbarItemCount);
    }
    finally
    {
        root.Delete(true);
    }
}
```

- [ ] **2단계: 재생 성공 후 대표 이동 실패 테스트 작성**

```csharp
[TestMethod]
public async Task SeasonHeroPlayback_AdvancesAndRestartsAfterSavedHistory()
{
    var root = Directory.CreateTempSubdirectory("dabom-season-hero-play-");
    try
    {
        var paths = Enumerable.Range(1, 4)
            .Select(number => Path.Combine(root.FullName, $"Episode {number}.mkv"))
            .ToArray();
        var playedAt = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        var data = CachedData(root.FullName, paths);
        for (var index = 0; index < paths.Length; index++)
        {
            data.VideosByPath[paths[index]] = TvRecord(
                $"Episode {index + 1}", "시리즈", 1, index + 1, "10") with
            {
                LastPlayedUtc = index < 2 ? playedAt : null
            };
        }
        var vm = new MainViewModel(
            new LibraryStore(root.FullName),
            new StubScanner(paths),
            data,
            _ => true,
            () => DateTimeOffset.Parse("2026-08-08T00:00:00Z"),
            _ => 0);
        await vm.ScanAsync();
        var featured = vm.FeaturedVideo;
        Assert.IsTrue(vm.OpenSeason(
            vm.VisibleItems.OfType<SeasonItemViewModel>().Single()));

        await vm.PlayAsync(vm.HeroVideo!);
        Assert.AreEqual(paths[3], vm.HeroVideo!.Path);

        await vm.PlayAsync(vm.HeroVideo!);
        Assert.AreEqual(paths[0], vm.HeroVideo!.Path);
        Assert.AreEqual("처음부터 보기", vm.ActiveSeason!.IntroLabel);
        Assert.AreSame(featured, vm.FeaturedVideo);
    }
    finally
    {
        root.Delete(true);
    }
}
```

- [ ] **3단계: 재생 이력 저장 실패 시 Hero 유지 테스트 작성**

```csharp
[TestMethod]
public async Task SeasonHeroPlayback_WhenHistorySaveFails_KeepsCurrentIntro()
{
    var root = Directory.CreateTempSubdirectory("dabom-season-hero-save-");
    try
    {
        var first = Path.Combine(root.FullName, "Episode 1.mkv");
        var second = Path.Combine(root.FullName, "Episode 2.mkv");
        var data = CachedData(root.FullName, first, second);
        data.VideosByPath[first] = TvRecord("Episode 1", "시리즈", 1, 1, "10");
        data.VideosByPath[second] = TvRecord("Episode 2", "시리즈", 1, 2, "10");
        var store = new LibraryStore(
            root.FullName,
            (_, _, _) => throw new IOException("disk full"));
        var vm = new MainViewModel(
            store,
            new StubScanner(first, second),
            data,
            _ => true,
            () => DateTimeOffset.Parse("2026-08-08T00:00:00Z"),
            _ => 0);
        await vm.ScanAsync();
        Assert.IsTrue(vm.OpenSeason(
            vm.VisibleItems.OfType<SeasonItemViewModel>().Single()));
        var intro = vm.HeroVideo;

        await vm.PlayAsync(intro!);

        Assert.AreSame(intro, vm.HeroVideo);
        Assert.IsNull(intro!.Record.LastPlayedUtc);
        StringAssert.Contains(vm.StatusMessage, "재생 이력 저장 실패");
    }
    finally
    {
        root.Delete(true);
    }
}
```

- [ ] **4단계: 신규 ViewModel 테스트가 예상대로 실패하는지 확인**

실행:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~SeasonView_UsesWholeGroupHeroAndContextAcrossSearch|FullyQualifiedName~SeasonHeroPlayback_"
```

예상: `ActiveSeason`, `HeroVideo`, `ToolbarContextLabel`, `ToolbarItemCount`가 없어 컴파일 실패한다.

- [ ] **5단계: 활성 시즌과 현재 Hero 속성 구현**

`MainViewModel`의 시즌 상태와 Featured 상태 옆에 아래 속성을 추가한다.

```csharp
private SeasonItemViewModel? _activeSeason;
public SeasonItemViewModel? ActiveSeason
{
    get => _activeSeason;
    private set
    {
        if (!Set(ref _activeSeason, value)) return;
        Raise(nameof(HeroVideo));
        RefreshCommandStates();
    }
}

public VideoItemViewModel? HeroVideo => ActiveSeason?.IntroEpisode ?? FeaturedVideo;
public string ToolbarContextLabel => IsSeasonView ? "에피소드" : "내 영상";
public int ToolbarItemCount => IsSeasonView ? DisplayItemCount : VisibleCount;
public string ToolbarGuidance => IsSeasonView
    ? "현재 조건의 에피소드를 표시하고 있습니다."
    : "현재 조건의 영상을 표시하고 있습니다.";
```

`FeaturedVideo` setter에서 추천 변경이 전체 목록 Hero에 반영되도록 `HeroVideo` 알림을 추가한다.

```csharp
if (Set(ref _featuredVideo, value))
{
    Raise(nameof(HeroVideo));
    RefreshCommandStates();
}
```

- [ ] **6단계: 기존 Hero 명령을 현재 Hero 영상에 연결**

별도 명령을 만들지 않고 생성자의 `PlayFeaturedCommand`만 다음처럼 바꾼다.

```csharp
PlayFeaturedCommand = new AsyncRelayCommand(
    () => HeroVideo is { } video ? PlayAsync(video) : Task.CompletedTask,
    () => CanMutateLibrary && HeroVideo is not null);
```

- [ ] **7단계: 목록 재구성에서 활성 시즌 소개를 전체 그룹으로 생성**

`RebuildVisibleItems`에서 그룹 해체 확인 뒤, 활성 시즌 분기와 전체 목록 분기를 다음 원칙으로 변경한다.

```csharp
if (_activeSeasonKey is { } activeKey)
{
    var wholeGroup = groups[activeKey];
    ActiveSeason = new SeasonItemViewModel(activeKey, wholeGroup, wholeGroup);
    var activeEpisodes = matching
        .Where(video => SeasonGroupKey.From(video.Record) == activeKey)
        .ToArray();
    if (activeEpisodes.Length > 0)
    {
        _seasonDisplayTitle = activeEpisodes[0].Record.SeriesTitle!.Trim();
    }
    items.AddRange(activeEpisodes);
}
else
{
    ActiveSeason = null;
}
```

`else` 안에서는 현재 `foreach (var video in matching)`부터 끝나는 전체 목록 투영문을 `ActiveSeason = null;` 바로 뒤에 내용 변경 없이 유지한다.

`VisibleItems`를 채운 뒤 공통 툴바 수를 항상 알린다.

```csharp
Raise(nameof(DisplayItemCount));
Raise(nameof(ToolbarItemCount));
Raise(nameof(SeasonHeading));
```

시즌 진입, 명시적 복귀와 자동 그룹 해체가 같은 문맥 알림을 사용하도록 아래 메서드를 추가하고 기존 중복 `Raise` 호출을 교체한다.

```csharp
private void RaiseSeasonContext()
{
    Raise(nameof(IsSeasonView));
    Raise(nameof(SeasonHeading));
    Raise(nameof(ToolbarContextLabel));
    Raise(nameof(ToolbarItemCount));
    Raise(nameof(ToolbarGuidance));
}
```

`OpenSeason`과 `CloseSeason`은 `RefreshLibraryView(false)` 다음에 `RaiseSeasonContext()`를 호출한다. `RebuildVisibleItems`의 `wasSeasonView != IsSeasonView` 분기도 `RaiseSeasonContext()`를 호출한다.

- [ ] **8단계: 재생 이력 저장 성공 뒤 시즌 소개 재계산**

`PlayAsync`의 저장 성공 분기에서 기존 `video.Update` 직후 활성 시즌일 때만 기존 메모리 목록 재구성을 호출한다.

```csharp
_data = next;
video.Update(updated, _store);
if (IsSeasonView) RefreshLibraryView(false);
StatusMessage = "영상을 기본 앱으로 실행했습니다.";
```

실행 실패와 저장 실패 분기는 호출하지 않아 현재 Hero를 유지한다.

- [ ] **9단계: ViewModel 관련 테스트 통과 확인**

실행:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~SeasonView_|FullyQualifiedName~SeasonHeroPlayback_|FullyQualifiedName~PlayAsync_"
```

예상: 시즌 Hero 신규 테스트와 기존 시즌·재생 실패 회귀 테스트가 모두 통과한다.

- [ ] **10단계: 작업 2 커밋**

```powershell
git add src/Dabom/Main/MainViewModel.cs tests/Dabom.Tests/MainViewModelTests.cs
git commit -m "feat: expose active season hero state"
```

---

### 작업 3: 시즌 리본, Hero, 공통 툴바와 그룹 팝업 UI

**파일:**

- 수정: `src/Dabom/MainWindow.xaml:138-239,307-349,676-738,850-983`
- 수정: `src/Dabom/MainWindow.xaml.cs:289-332`
- 테스트: `tests/Dabom.Tests/MainWindowMarkupTests.cs:227-265,539-605`

**인터페이스:**

- 소비: 작업 1의 `SeasonItemViewModel.TotalSummary`, `IntroLabel`, `IntroHeading`, `IntroEpisode`
- 소비: 작업 2의 `ActiveSeason`, `HeroVideo`, `ToolbarContextLabel`, `ToolbarItemCount`, `ToolbarGuidance`
- 유지: `OnReturnToLibrary`, `PlayFeaturedCommand`, `CardPopup`, `PlaceCardPopup`, `OnCardMove`, `OnCardLeave`

- [ ] **1단계: 시즌 리본과 형식별 팝업 실패 테스트 작성**

기존 `LibraryGrid_BindsTypedVideoAndSeasonItemsWithAccessibleSeasonAction`에 `SeasonTypeRibbon` 검사를 추가하고 아래 테스트를 새로 작성한다.

```csharp
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
```

- [ ] **2단계: 시즌 Hero와 공통 툴바 실패 테스트 작성**

기존 `LibraryToolbar_ExposesSeasonLocationAndAccessibleReturnControl`을 `SeasonHero_ExposesContextAndAccessibleReturnControl`로 바꾸고 Hero 범위에서 바인딩을 검사한다. 공통 툴바 검사는 별도 테스트로 둔다.

```csharp
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
```

- [ ] **3단계: UI 테스트가 예상대로 실패하는지 확인**

실행:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~CardPopup_UsesVideoAndSeasonTemplatesAndAllowsSeasonHover|FullyQualifiedName~SeasonHero_ExposesContextAndAccessibleReturnControl|FullyQualifiedName~LibraryToolbar_UsesSameControlsWithContextBindings"
```

예상: 신규 이름과 바인딩이 현재 XAML·코드비하인드에 없어 실패한다.

- [ ] **4단계: 시즌 카드 배지를 전체 폭 리본으로 변경**

시즌 카드 포스터 안의 기존 작은 배지를 다음 구조로 교체한다. 포스터와 `NO POSTER`는 그대로 둔다.

```xml
<Border x:Name="SeasonTypeRibbon"
        HorizontalAlignment="Stretch"
        VerticalAlignment="Top"
        Padding="0,8"
        CornerRadius="15,15,0,0"
        Background="{StaticResource AccentBrush}">
    <TextBlock Text="TV 시즌"
               HorizontalAlignment="Center"
               Foreground="{StaticResource PageBrush}"
               FontSize="12"
               FontWeight="Bold" />
</Border>
```

카드 아래 `DisplayTitle`과 기존 현재 결과용 `Summary`는 변경하지 않는다.

- [ ] **5단계: Featured Hero를 현재 `HeroVideo`에 연결하고 시즌 콘텐츠 추가**

Hero Border의 null 표시 조건과 공통 표면 DataContext를 각각 `HeroVideo`로 바꾼다.

```xml
<DataTrigger Binding="{Binding HeroVideo}" Value="{x:Null}">
    <Setter Property="Visibility" Value="Collapsed" />
</DataTrigger>

<Grid x:Name="FeaturedHeroSurface" DataContext="{Binding HeroVideo}">
```

기존 Featured 텍스트 StackPanel에 `x:Name="FeaturedHeroContent"`를 주고 `IsSeasonView=True`일 때만 숨긴다. 같은 Grid.Column에 아래 시즌 StackPanel을 추가한다.

```xml
<StackPanel x:Name="SeasonHeroContent"
            Grid.Column="1"
            Margin="50,0,0,0"
            VerticalAlignment="Center"
            DataContext="{Binding DataContext,
                RelativeSource={RelativeSource AncestorType=Window}}">
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsSeasonView}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>
    <Button Content="← 전체 영상"
            HorizontalAlignment="Left"
            AutomationProperties.Name="전체 영상으로 돌아가기"
            Click="OnReturnToLibrary" />
    <TextBlock Text="TV 시즌"
               Margin="0,20,0,0"
               Foreground="{StaticResource AccentBrush}"
               FontWeight="SemiBold" />
    <TextBlock Text="{Binding SeasonHeading}"
               FontSize="48"
               FontWeight="SemiBold"
               TextWrapping="Wrap"
               AutomationProperties.LiveSetting="Polite" />
    <TextBlock Margin="0,8,0,0" Foreground="{StaticResource MutedBrush}">
        <Run Text="총 " />
        <Run Text="{Binding ActiveSeason.TotalEpisodeCount, Mode=OneWay}" />
        <Run Text="편" />
    </TextBlock>
    <TextBlock Text="{Binding ActiveSeason.IntroLabel}"
               Margin="0,18,0,0"
               Foreground="{StaticResource AccentBrush}"
               FontWeight="SemiBold" />
    <TextBlock Text="{Binding ActiveSeason.IntroHeading}"
               Margin="0,6,0,0"
               FontSize="24"
               FontWeight="SemiBold" />
    <TextBlock Text="{Binding ActiveSeason.IntroEpisode.Record.Synopsis}"
               Margin="0,12,0,0"
               MaxWidth="650"
               HorizontalAlignment="Left"
               Foreground="{StaticResource MutedBrush}"
               TextWrapping="Wrap" />
    <Button Style="{StaticResource PrimaryActionButtonStyle}"
            Command="{Binding PlayFeaturedCommand}"
            Margin="0,22,0,0"
            HorizontalAlignment="Left"
            AutomationProperties.Name="대표 에피소드 재생하기">
        <TextBlock Text="재생하기" />
    </Button>
</StackPanel>
```

왼쪽 포스터와 배경은 공통 `HeroVideo.Poster`를 사용하므로 시즌 상세에서는 대표 에피소드 포스터, 전체 목록에서는 기존 추천 포스터를 자동 표시한다.

- [ ] **6단계: 검색 툴바의 조건부 시즌 행 제거 및 문맥 바인딩 적용**

`LibraryToolbar` 안의 `IsSeasonView` 조건부 복귀 StackPanel을 제거하고, 기존 검색·필터·정렬 행을 유일한 행으로 유지한다. 왼쪽 문맥 텍스트만 다음처럼 바꾼다.

```xml
<TextBlock Text="{Binding ToolbarContextLabel}"
           FontSize="10"
           FontWeight="SemiBold"
           Foreground="{StaticResource AccentBrush}"
           VerticalAlignment="Center" />
<TextBlock Margin="6,0,0,0" FontSize="22" FontWeight="SemiBold">
    <Run Text="{Binding ToolbarItemCount, Mode=OneWay}" />
    <Run Text="편" />
</TextBlock>
<TextBlock Grid.Row="1"
           Text="{Binding ToolbarGuidance}"
           FontSize="9"
           Foreground="{StaticResource MutedBrush}" />
```

검색창, 필터 ComboBox와 정렬 ComboBox의 XAML은 이동하거나 복제하지 않는다.

- [ ] **7단계: 기존 CardPopup에 영상·시즌 DataTemplate 적용**

기존 Popup 외곽 Border 안의 `Grid x:Name="CardPopupSurface"` 시작 태그를 `ContentControl x:Name="CardPopupSurface" Content="{Binding}"`으로 교체한다. 현재 `Grid.ColumnDefinitions`부터 마지막 `StackPanel`까지의 영상 콘텐츠는 자식 값과 바인딩을 바꾸지 않고 `DataTemplate DataType="{x:Type main:VideoItemViewModel}"`의 단일 `Grid` 안으로 이동한다. 그 영상 템플릿 다음에 아래 시즌 템플릿을 추가한다.

```xml
<DataTemplate DataType="{x:Type main:SeasonItemViewModel}">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="126" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Border CornerRadius="16,0,0,16"
                        Background="{StaticResource SurfaceBrush}">
                    <Grid>
                        <Border Margin="1" CornerRadius="15,0,0,15">
                            <Border.Background>
                                <ImageBrush ImageSource="{Binding Poster}"
                                            Stretch="UniformToFill" />
                            </Border.Background>
                        </Border>
                        <TextBlock Text="NO POSTER"
                                   HorizontalAlignment="Center"
                                   VerticalAlignment="Center"
                                   Foreground="{StaticResource MutedBrush}">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Visibility" Value="Collapsed" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding HasPoster}" Value="False">
                                            <Setter Property="Visibility" Value="Visible" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>
                    </Grid>
                </Border>
                <StackPanel Grid.Column="1" Margin="25,23,25,21">
                    <TextBlock Text="TV 시즌 그룹"
                               FontSize="10"
                               FontWeight="SemiBold"
                               Foreground="{StaticResource AccentBrush}" />
                    <TextBlock Text="{Binding DisplayTitle}"
                               Margin="0,4,0,4"
                               FontSize="22"
                               FontWeight="SemiBold"
                               TextWrapping="Wrap" />
                    <TextBlock Text="{Binding TotalSummary}"
                               Foreground="{StaticResource MutedBrush}" />
                    <TextBlock Text="{Binding IntroLabel}"
                               Margin="0,15,0,0"
                               Foreground="{StaticResource AccentBrush}"
                               FontWeight="SemiBold" />
                    <TextBlock Text="{Binding IntroHeading}"
                               Margin="0,5,0,0"
                               FontWeight="SemiBold" />
                    <TextBlock Text="{Binding IntroEpisode.Record.Synopsis}"
                               Margin="0,10,0,0"
                               FontSize="11"
                               LineHeight="18"
                               Foreground="{StaticResource MutedBrush}"
                               TextWrapping="Wrap" />
                    <TextBlock Text="클릭 또는 Enter로 시즌 열기"
                               Margin="0,14,0,0"
                               FontSize="10"
                               Foreground="{StaticResource MutedBrush}" />
                </StackPanel>
            </Grid>
</DataTemplate>
```

두 `DataTemplate`은 `ContentControl.Resources` 안에 두고, 리소스 뒤에 빈 본문을 추가하지 않는다. `Content="{Binding}"`이 현재 Popup DataContext 형식에 맞는 템플릿을 자동 선택한다.

영상 템플릿은 기존 표시 필드와 이름을 그대로 보존한다. 시즌 템플릿에는 그룹 전체를 대표하지 않는 파일명, 상영일, 감독과 배우를 넣지 않는다.

- [ ] **8단계: hover 대상의 영상 전용 제한 제거**

`OnCardEnter`의 `VideoItemViewModel` 가드를 제거하고 ListBox의 두 항목 형식을 모두 기존 Popup 흐름에 전달한다.

```csharp
private void OnCardEnter(object sender, MouseEventArgs e)
{
    var card = (ListBoxItem)sender;
    _hoveredCard = card;
    UpdateCardPopupPointerPlacement(card, e);
    RefreshCardPopup();
}
```

`OnVideoDoubleClick`의 영상 형식 가드와 `OnCardClick`의 시즌 진입 분기는 변경하지 않는다.

- [ ] **9단계: UI·입력 회귀 테스트 통과 확인**

실행:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~MainWindowMarkupTests"
```

예상: 시즌 리본, Hero, 공통 툴바, 팝업 신규 검사와 기존 포인터 배치·시즌 진입·복귀·접근성 검사가 모두 통과한다.

- [ ] **10단계: Debug 빌드 확인**

실행:

```powershell
dotnet build Dabom.sln -c Debug --no-restore --nologo
```

예상: 경고 0개, 오류 0개.

- [ ] **11단계: 작업 3 커밋**

```powershell
git add src/Dabom/MainWindow.xaml src/Dabom/MainWindow.xaml.cs tests/Dabom.Tests/MainWindowMarkupTests.cs
git commit -m "feat: render season context hero and popups"
```

---

## 최종 검증

- [ ] **1단계: 전체 조용한 테스트 실행**

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo
```

예상: 기존 237개와 신규 6개를 합한 243개 테스트 통과, 실패 0개, 건너뜀 0개.

- [ ] **2단계: 전체 Debug 빌드 실행**

```powershell
dotnet build Dabom.sln -c Debug --no-restore --nologo
```

예상: 경고 0개, 오류 0개.

- [ ] **3단계: 지식 그래프 갱신**

```powershell
graphify update .
```

예상: `SeasonItemViewModel`의 대표 에피소드 속성과 `MainViewModel`의 활성 Hero 관계가 `graphify-out/graph.json`에 반영된다.

- [ ] **4단계: 변경 범위와 공백 오류 확인**

```powershell
git diff --check 21da155..HEAD
git diff --name-only 21da155..HEAD
git status --short
```

예상 tracked 변경 범위:

```text
src/Dabom/Main/SeasonItemViewModel.cs
src/Dabom/Main/MainViewModel.cs
src/Dabom/MainWindow.xaml
src/Dabom/MainWindow.xaml.cs
tests/Dabom.Tests/MainViewModelTests.cs
tests/Dabom.Tests/MainWindowMarkupTests.cs
```

`git diff --check` 출력이 없고, 계획 밖 tracked 파일 변경이 없어야 한다. `graphify-out/`과 `.superpowers/`는 저장소의 기존 ignore 정책을 유지한다.

- [ ] **5단계: 선택 수동 검증은 별도 승인 전 실행하지 않기**

실제 WPF 창에서 Hero 전환, 포인터 팝업, Tab·Esc 포커스와 스크린 리더 낭독을 확인하는 검증은 사용자 세션에 영향을 줄 수 있으므로 별도 승인이 있을 때만 수행한다.
