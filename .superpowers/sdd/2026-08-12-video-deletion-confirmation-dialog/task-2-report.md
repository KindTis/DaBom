# Task 2 보고서: Delete 키 확인 창 연결

## 구현 내용

- `MainWindow.OnVideoListKeyDown`의 Delete 키 흐름에서 기존 `MessageBox.Show` 호출을 제거했습니다.
- `PrepareVideoDeletion()`이 반환한 요청의 `Video.FileName`과 `Status`만 `VideoDeletionConfirmationWindow`에 전달합니다.
- 확인 창의 `Owner`를 현재 `MainWindow`로 지정하고, `ShowDialog() == true`일 때만 기존 `DeleteVideoAsync(request)`를 호출합니다.
- 시즌 삭제 안내, 요청 준비 실패 반환, `e.Handled`, 삭제·재검증·휴지통·오류 토스트 로직은 변경하지 않았습니다.

## TDD 증거

### RED

명령:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~DeleteKey_PreparesAndConfirmsVideoDeletionWithoutGlobalHandling"
```

핵심 출력:

```text
실패 DeleteKey_PreparesAndConfirmsVideoDeletionWithoutGlobalHandling
StringAssert.Contains이(가) 실패했습니다. ... 'new VideoDeletionConfirmationWindow(' 문자열을 포함하지 않습니다.
```

기존 구현이 `MessageBox.Show`와 `request.Video.Path`를 사용하고 있어 새 계약 테스트가 의도대로 실패했습니다.

### GREEN

명령:

```powershell
dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo --filter "FullyQualifiedName~DeleteKey_PreparesAndConfirmsVideoDeletionWithoutGlobalHandling|FullyQualifiedName~VideoDeletionConfirmationWindow"
```

핵심 출력:

```text
통과! - 실패: 0, 통과: 3, 건너뜀: 0, 전체: 3
```

## 전체 테스트 및 빌드

- 전체 테스트: `dotnet test tests/Dabom.Tests/Dabom.Tests.csproj -c Debug --no-restore --nologo`
  - 실패 0, 통과 291, 건너뜀 0
- Debug 빌드: `dotnet build Dabom.sln -c Debug --no-restore --nologo`
  - 경고 0, 오류 0
- Release 빌드: `dotnet build Dabom.sln -c Release --no-restore --nologo`
  - 경고 0, 오류 0
- `graphify update .` 완료
- `git diff --check` 공백 오류 없음

## 변경 파일

- `src/Dabom/MainWindow.xaml.cs`
- `tests/Dabom.Tests/MainWindowMarkupTests.cs`
- 본 보고서

## 자체 검토

- 전체 경로(`request.Video.Path`)는 확인 창에 전달하거나 표시하지 않습니다.
- 취소 시 `DeleteVideoAsync`가 호출되지 않으므로 ViewModel과 파일 시스템 변경이 발생하지 않습니다.
- 기존 삭제 메서드와 파일 작업 코드는 건드리지 않았습니다.
- 실제 WPF 창을 띄우는 수동 검증은 수행하지 않았습니다.

## 우려 사항

- 현재 테스트는 소스 마크업 계약을 검사하며 실제 창 상호작용은 검증하지 않습니다. 실제 화면 배치와 키보드 동작은 별도 승인 후 수동 검증이 필요합니다.
