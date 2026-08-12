# 작업 5 보고서: 파일 없음 카드와 DEL 확인 UI

## RED 증거

다음 명령을 실행했다.

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~MissingFileCards|FullyQualifiedName~DeleteKey"
```

두 테스트가 기대대로 실패했다.

- `MissingFileCardsDimOnlyPosterAndKeepStatusRibbonOpaque`: `PosterOpacity` 바인딩 0개 (기대 2개)
- `DeleteKey_PreparesAndConfirmsVideoDeletionWithoutGlobalHandling`: `OnVideoListKeyDown`에 `Key.Delete` 처리 없음

## GREEN 및 최종 검증

다음 GREEN 범위가 통과했다.

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~MainWindowMarkupTests|FullyQualifiedName~SeasonItem_"
```

56개 통과. 최종으로 전체 테스트 286개 통과, `dotnet build src/Dabom/Dabom.csproj -c Debug --no-restore --nologo`는 경고·오류 0개로 통과했다. `git diff --check`도 통과했다. GUI 창과 MessageBox는 실행하지 않았다.

## 변경 파일

- `src/Dabom/MainWindow.xaml`
- `src/Dabom/MainWindow.xaml.cs`
- `src/Dabom/Main/MainViewModel.cs`
- `tests/Dabom.Tests/MainWindowMarkupTests.cs`

## 디자인 self-critique

- 포스터 배경 `Border` 두 곳에만 `PosterOpacity`를 적용했다. 하단 빨간 상태 띠와 기존 시즌 상단 띠는 같은 `Grid`의 형제이므로 불투명하게 유지된다.
- 기존 카드 크기·브러시·타이포그래피·상단 시즌 띠를 변경하지 않았고, 새 토큰이나 스타일을 추가하지 않았다.
- 기존 `AutomationName` 바인딩을 보존했다.

## 일반 self-review

- Delete는 `VideoList.KeyDown`에서 Enter보다 먼저만 처리하며, 전역 preview 처리에는 추가하지 않았다.
- 시즌은 확인창 없이 공통 토스트 안내를 요청한다. 영상은 live `PrepareVideoDeletion()` 결과에 따라 확인 문구를 선택하고, 실제 삭제는 `DeleteVideoAsync`에 맡긴다.
- 정적 테스트는 키 처리와 마크업 계층을 검증한다. GUI를 띄우지 않는 제약상 실제 MessageBox 상호작용은 수동 확인 대상으로 남는다.
