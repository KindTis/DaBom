# DABOM 동영상 관리자 구현 항목 매핑

## 구현 항목 매핑표

| 구현 항목 ID | 필수 여부 | 구현 계획서 항목 | 계획서 근거 | 수용 기준 | 구현 대상 | 구현 상태 | 검증 방법 | 검증 결과 | 항목 판정 | 보류 사유 |
|---|---|---|---|---|---|---|---|---|---|---|
| IMP-001 | 필수 | 솔루션과 프로젝트 기반 | Task 1 Step 1~2 | `net9.0-windows` WPF 앱과 MSTest 프로젝트가 같은 솔루션에서 복원·빌드된다. 테스트가 앱 internals에 접근한다. | `Dabom.sln`, `src/Dabom/Dabom.csproj`, `src/Dabom/App.xaml*`, `src/Dabom/Properties/AssemblyInfo.cs`, `tests/Dabom.Tests/Dabom.Tests.csproj` | 구현 완료 | `dotnet restore`; `dotnet build Dabom.sln -c Debug` | 통과 — 최종 Debug 빌드 경고 0, 오류 0 | 만족 |  |
| IMP-002 | 필수 | 데이터 모델과 순수 규칙 | Task 1 Step 3~7 | JSON 모델·스캔 계약·정렬 열거형이 존재하고 제목 대체, 누적 시·분, NFKC 검색, Featured 선택 규칙이 계획대로 동작한다. | `src/Dabom/Library/LibraryData.cs`, `LibraryRules.cs`, `tests/Dabom.Tests/LibraryRulesTests.cs` | 구현 완료 | RED 후 `dotnet test ... --filter LibraryRulesTests` | 통과 — RED `CS0234`; GREEN 4/4, 최종 전체 49/49 | 만족 |  |
| IMP-003 | 필수 | 재귀 라이브러리 스캐너 | Task 2 Step 1~3 | 지원 확장자만 재귀 탐색하고 중복·reparse point를 건너뛰며 크기와 수정일이 모두 같을 때만 재생시간을 재사용한다. 복구 가능한 파일 시스템 오류는 경고로 남기고 계속한다. 재생시간 판독기가 예외를 던져도 해당 영상은 `DurationTicks = null`로 포함하고 다른 파일 탐색을 계속한다. | `src/Dabom/Library/ILibraryScanner.cs`, `LibraryScanner.cs`, `tests/Dabom.Tests/LibraryScannerTests.cs` | 구현 완료 | RED 후 `dotnet test ... --filter LibraryScannerTests`; 예외를 던지는 duration delegate로 해당 영상 포함·null·다른 영상 탐색 지속 확인 | 통과 — RED `CS0246`; 스캐너 5개와 duration 예외 지속 테스트 포함 관련 6/6, 연결 해제·권한 거부 위치도 정상 위치 스캔을 막지 않음 | 만족 |  |
| IMP-004 | 필수 | Windows 재생시간 판독 | Task 2 Step 4~5 | Windows Property System `System.Media.Duration`의 `VT_UI8`를 읽고 COM/PROPVARIANT 자원을 해제하며 ABI 크기가 24다. | `src/Dabom/Library/WindowsDurationReader.cs`, `tests/Dabom.Tests/WindowsDurationReaderTests.cs` | 구현 완료 | `dotnet test ... --filter WindowsDurationReaderTests`와 실제 지원 영상 수동 확인 | 통과 — ABI 1/1; 실제 MP4에서 `DurationTicks=50,550,000`을 읽고 메인·편집 창에 동일하게 `0분` 표시 | 만족 |  |
| IMP-005 | 필수 | 포스터 디코딩 | Task 3 Step 3 | 유효 이미지를 240px 이하로 디코딩·Freeze하고 없는 파일·손상 이미지는 예외 없이 `null`이다. | `src/Dabom/Library/PosterImage.cs`, `tests/Dabom.Tests/PosterImageTests.cs` | 구현 완료 | RED 후 `dotnet test ... --filter PosterImageTests` | 통과 — RED 타입 부재; 대형 이미지 축소·Freeze와 손상 이미지 2/2, 실제 없는 포스터는 `NO POSTER` 표시 | 만족 |  |
| IMP-006 | 필수 | JSON 로드·정규화·손상 복구 | Task 3 Step 1~4 | 전체 상태가 왕복되고 경로가 정규화된다. 손상 JSON은 백업 뒤 빈 상태, 읽기 실패나 백업 실패는 원본을 보존하고 저장을 비활성화한다. | `src/Dabom/Library/LibraryStore.cs`, `tests/Dabom.Tests/LibraryStoreTests.cs` | 구현 완료 | RED 후 `dotnet test ... --filter LibraryStoreTests`; 실제 손상·잠금 시작 | 통과 — 저장소 8/8; 실제 손상 15바이트 원본을 타임스탬프 백업했고 잠금 시작에서 저장·변경을 비활성화하며 오류 경로 유지 | 만족 |  |
| IMP-007 | 필수 | 원자 JSON 저장 | Task 3 Step 1, 4 | 같은 폴더의 임시 파일을 완전히 쓴 뒤 교체하고 실패 시 기존 JSON과 기존 메모리 상태를 보존하며 임시 파일을 정리한다. | `LibraryStore.SaveAsync`, 저장 실패 테스트 | 구현 완료 | `SaveAsync_WhenCommitFails_KeepsExistingJson` 및 전체 저장소 테스트 | 통과 — 커밋 실패 뒤 기존 위치·바이트 유지 및 임시 파일 0개 | 만족 |  |
| IMP-008 | 필수 | 포스터 수명주기와 경로 경계 | Task 3 Step 5~6 | JPG/JPEG/PNG/BMP 실제 이미지만 GUID 이름으로 복사한다. 상대 경로는 posters 하위만 해석하고 교체·제거 커밋 뒤 이전 포스터만 삭제한다. | `LibraryStore.ImportPosterAsync`, `ResolvePosterPath`, `DeletePoster`, 관련 테스트 | 구현 완료 | `dotnet test ... --filter "LibraryStoreTests|PosterImageTests"` | 통과 — 유효 이미지 복사·삭제, 확장자 거부, malformed/escape/absolute 경로 거부 포함 관련 10/10 | 만족 |  |
| IMP-009 | 필수 | 최소 ViewModel 기반과 영상 표시 항목 | Task 4 Step 3~4 | 알림과 동기·비동기 명령 기반이 최소 구현되고, 영상 항목이 표시 파생 상태·검색·포스터 대체를 제공하며 같은 경로 객체를 재사용한다. | `src/Dabom/Main/ViewModelSupport.cs`, `VideoItemViewModel.cs`, 관련 테스트 | 구현 완료 | `dotnet test ... --filter MainViewModelTests` | 통과 — missing poster, 같은 항목 재사용 포함 관련 테스트와 최종 전체 49/49 | 만족 |  |
| IMP-010 | 필수 | 스캔 결과 결합 | Task 4 Step 5~6 | 저장 기록을 보존한 채 현재 스캔 경로만 화면에 반영하고 캐시가 바뀐 경우만 저장한다. 최신 경고·시각·Featured·상태를 완료 후 한 번에 갱신한다. | `src/Dabom/Main/MainViewModel.cs`, `MainViewModelTests.cs` | 구현 완료 | 스캔 결합·경고·저장 호출 테스트와 전체 `MainViewModelTests` | 통과 — 객체 재사용·최신 경고 교체·로컬 시각·Featured·상태의 완료 시점 일괄 반영 확인 | 만족 |  |
| IMP-011 | 필수 | 검색·정렬·선택 | Task 4 Step 1, 7 | NFKC 검색 결과에서 제외된 선택만 해제하고 검색 해제 시 재선택하지 않는다. 제목 오름차순, 개봉일·수정일 내림차순이며 정렬 변경은 선택을 유지한다. | `MainViewModel.SearchText`, `SelectedSort`, `VisibleVideos`, 관련 테스트 | 구현 완료 | 기본 정렬·선택·메타데이터 편집 후 재필터/재정렬 테스트와 실제 UI 순서 | 통과 — 검색 제외 시 선택 해제·해제 후 미재선택, 기본 제목·개봉일 내림차순, 1,000개 정렬에서 예상 첫 항목 `검증 영상 0840` 확인 | 만족 |  |
| IMP-012 | 필수 | 위치 변경 트랜잭션 | Task 4 Step 1, 8~9 | 위치 추가·제거는 저장 성공 뒤 확정하고 즉시 한 번 재탐색한다. 중복·저장 실패·동시 변경은 저장 데이터와 화면을 보존한다. 위치를 제거해도 원본 영상, `_data.VideosByPath`의 메타데이터·캐시·이력, 관리 포스터 파일은 삭제하지 않는다. | `MainViewModel.AddLocationAsync`, `RemoveLocationAsync`, 관련 테스트 | 구현 완료 | 위치 재로드·롤백·동시성 테스트; 제거 전후 JSON 영상 기록·포스터 파일·원본 영상 해시 유지 확인 | 통과 — 저장 실패 롤백, 재로드, 동시 변경 거부, 후속 스캔 1회, 원본·JSON 기록·포스터 보존 | 만족 |  |
| IMP-013 | 필수 | 저장 불가 상태 | Task 4 Step 1, 5, 8 | `LibraryStore.CanSave == false`이면 모든 변경 명령과 직접 변경 호출이 거부되고 최초 로드 경고 경로가 유지된다. | `MainViewModel.CanMutateLibrary`, 상태/명령 테스트 | 구현 완료 | 저장 불가 fixture의 `MainViewModelTests`와 `CommandStateTests`, 잠긴 정상 JSON 실제 시작 | 통과 — 직접 위치 변경과 전체 변경 명령·버튼 거부, scanner 0회, F1 뒤에도 원래 오류 경로 유지 | 만족 |  |
| IMP-014 | 필수 | 기본 앱 재생과 이력 트랜잭션 | Task 5 Step 1, 4 | 기본 앱 실행 성공 뒤에만 이력을 저장한다. 실행 실패 또는 이력 저장 실패 시 기존 이력·Featured·메모리를 유지하고 오류를 구분한다. 성공 시에도 현재 Featured는 유지하고 다음 앱 시작 또는 스캔 완료 때만 재선정한다. 동시 실행은 한 번만 허용한다. | `MainViewModel.PlayAsync`, launcher seam, 관련 테스트 | 구현 완료 | 재생 실패·저장 실패·성공 뒤 Featured 동일성·다음 스캔 재선정·동시성 테스트 및 실제 기본 앱 수동 실행 | 통과 — 자동 롤백·단일 커밋·잠금·Featured 재선정 규칙; 일반 더블클릭·Enter·Featured 버튼으로 기본 연결 앱이 실제 파일을 열고 이력 저장, 연결 실패 때 이력 불변 | 만족 |  |
| IMP-015 | 필수 | 메타데이터 편집 초안 | Task 5 Step 2, 5 | 편집 초안이 모든 필드·파일 정보·포스터 미리보기를 제공하고 저장 중 중복 저장을 막으며 실패 뒤 입력과 미리보기를 유지한다. | `src/Dabom/Metadata/MetadataEditorViewModel.cs`, `MetadataEditorViewModelTests.cs` | 구현 완료 | RED 후 `dotnet test ... --filter MetadataEditorViewModelTests`; 실제 실패 창 | 통과 — 전체 필드·파일 정보·미리보기, 실패 뒤 `저장 실패 초안`과 선택 포스터 유지, 동시 저장 거부 | 만족 |  |
| IMP-016 | 필수 | 메타데이터 저장 트랜잭션 | Task 5 Step 2, 6~7 | 새 포스터 복사 → JSON 커밋 → 이전 포스터 삭제 순서를 지킨다. JSON 성공 뒤 화면·검색·정렬을 갱신하며 사후 삭제 실패는 성공을 뒤집지 않는다. 새 포스터 복사 뒤 JSON 저장이 실패하면 이전 JSON·이전 포스터·공유 메모리 상태를 유지하고 편집 초안·선택 미리보기를 보존하며, 새 복사본이 미사용 파일로 남는 것은 계획된 한계로 허용한다. | `MainViewModel.CreateMetadataEditor`, `CommitMetadataAsync`, 관련 테스트 | 구현 완료 | 편집 실패 시 JSON·이전 포스터·메모리 불변 및 초안·미리보기 유지·새 미사용 복사본 잔존 확인; 검색 제외·재정렬·사후 정리 실패 테스트 | 통과 — 사후 삭제 경고는 성공 유지, JSON 실패 뒤 기존 상태·초안·미리보기 보존 및 계획된 미사용 새 복사본 1개 잔존 | 만족 |  |
| IMP-017 | 필수 | 명령 상태와 편집 이벤트 | Task 6 전체 | 스캔·위치 저장·이력 저장 중 변경 명령이 비활성화되고 완료 후 복구된다. 공개 명령은 `ICommand`, 편집 요청은 선택된 영상에만 발생한다. | `MainViewModel` 명령 속성·이벤트, `tests/Dabom.Tests/CommandStateTests.cs` | 구현 완료 | RED 후 `dotnet test ... --filter CommandStateTests`; 빌드 | 통과 — RED `CS1061`; 스캔·위치·이력 잠금, 저장 불가, 선택 편집 이벤트와 공개 `ICommand` 5/5; 빌드 경고 0·오류 0 | 만족 |  |
| IMP-018 | 필수 | 앱 조립과 초기화 | Task 7 Step 1 | `App`이 유일한 조립 루트이며 저장소 로드 경고와 최초 빈 상태를 전달하고 위치가 있을 때만 초기 스캔한다. | `src/Dabom/App.xaml`, `App.xaml.cs`, `MainViewModel.InitializeAsync` | 구현 완료 | 빌드, 초기화 단위 테스트, 빈 상태·정상 데이터·손상 데이터 앱 시작 | 통과 — 위치 없음 0회·위치 있음 1회 2/2; 세 시작 상태 실제 실행이 안정적이고 저장소 경고 전달 확인 | 만족 |  |
| IMP-019 | 필수 | WPF 테마 | Task 7 Step 2 | 지정된 다크 시네마 색·Segoe UI·컨트롤 스타일·Boolean 변환기를 순수 WPF 리소스로 제공한다. | `src/Dabom/Styles/DabomTheme.xaml` | 구현 완료 | XAML 빌드와 브라우저 레퍼런스·1440×1000 실제 창 육안 비교 | 통과 — `#0E0F10`·`#171819`·`#CBC9C2`, Segoe UI, 얇은 선과 다크 Button/TextBox/ComboBox를 실제 화면에서 확인 | 만족 |  |
| IMP-020 | 필수 | 메인 화면 구조와 빈 상태 | Task 7 Step 3 | 상단 바, Featured, 도구, 카드 목록, 상태 영역의 순서·2:3 비율·반응형 WrapPanel을 구현하고 위치 없음/영상 없음/경고 없음 상태를 올바르게 표시한다. | `src/Dabom/MainWindow.xaml`, `VideoItemViewModel.cs` | 구현 완료 | XAML 빌드, 레퍼런스 비교, 760×640 최소 창·1,000개 수동 확인 | 통과 — 다섯 영역·빈 상태·2:3 카드·열 수 변화·가로 잘림 없음, 1,000개와 페이지 마지막 상태 영역 표시 | 만족 |  |
| IMP-021 | 필수 | 팝업·키보드·포커스·재생 상호작용 | Task 7 Step 4~5 | 위치·경고·카드 상세 팝업, Ctrl+K/F1/Enter/Esc, 카드 hover/focus 우선순위, 외부 스크롤과 빈 공간 오작동 방지를 계획대로 구현한다. | `MainWindow.xaml`, `MainWindow.xaml.cs` | 구현 완료 | 빌드와 Task 9 키보드·팝업 수동 체크리스트, 420px 초과 경고 fixture | 통과 — Ctrl+K/Esc/F1/Enter, 위치·경고 팝업, 방향키 자동 스크롤, A 포커스→B hover→A 복원 확인; Popup HWND 클릭 통과 처리로 인접 카드 hover 정상; 경고 30개에서 341px 목록이 세로 스크롤 가능하고 0→100 이동 뒤 마지막 경로·원인이 표시됨 | 만족 |  |
| IMP-022 | 필수 | 배경 입자와 감소 애니메이션 | Task 7 Step 6 | 고정 seed의 WPF Ellipse 24개만 생성하고 Windows 애니메이션 설정이 꺼지면 정지한다. | `MainWindow.xaml.cs` | 구현 완료 | 빌드, 실행 화면, `SystemParameters.ClientAreaAnimation` 분기 검토 | 통과 — seed 1707의 Ellipse 24개만 생성; 애니메이션 허용 상태에서는 저강도 반복, 비허용 상태에서는 두 `BeginAnimation`을 건너뛰어 정지 | 만족 |  |
| IMP-023 | 필수 | 메타데이터 창 | Task 8 전체 | 전체 파일 정보와 편집 폼을 바인딩하고 유효 포스터 선택·제거·저장·취소를 제공한다. 저장 성공 때만 닫고 저장 중 닫기를 막으며 MainWindow 이벤트로 모달 연결한다. | `src/Dabom/MetadataWindow.xaml`, `.xaml.cs`, `MainWindow.xaml.cs` | 구현 완료 | 편집 테스트, XAML 빌드, 성공·실패 실제 모달 | 통과 — 전체 경로 읽기 전용, 파일 정보·포스터 미리보기 확인; Enter 저장 후 재열기 값 유지, 잠금 실패 때 창·입력·미리보기·JSON 유지 | 만족 |  |
| IMP-024 | 필수 | 레퍼런스 보존 검증 | Task 7 Step 7, Task 9 Step 1 및 Task 9의 기존 레퍼런스 3개 수정 금지 | 사용자 제공 `index.html`, `style.css`, `app.js`가 아래 변경 기준점의 시작 SHA-256과 동일함을 재현 가능하게 확인하고 명령이 `Reference design checks passed: 1/1`을 출력한다. | 새 `reference-design/verify.ps1`; 기존 레퍼런스 3개는 수정 금지 | 구현 완료 | `powershell -ExecutionPolicy Bypass -File reference-design/verify.ps1`; `Get-FileHash` 직접 재확인 | 통과 — 고정 SHA-256 세 파일이 시작값과 동일하고 `Reference design checks passed: 1/1` | 만족 |  |
| IMP-025 | 필수 | 자동 회귀 검증 | Task 9 Step 1, 6 | Debug 전체 빌드와 전체 MSTest, 레퍼런스 보존 검사가 모두 오류 없이 끝난다. | 솔루션 전체, 테스트 전체 | 구현 완료 | `dotnet build Dabom.sln -c Debug`; `dotnet test Dabom.sln -c Debug`; 보존 스크립트 | 통과 — Debug 빌드 경고 0·오류 0, MSTest 49/49, 레퍼런스 1/1 | 만족 |  |
| IMP-026 | 필수 | 저장 경계와 원본 불변 | Task 9 Step 2, 5 | 앱 생성 파일이 `%LocalAppData%\Dabom` 경계에 한정되고 테스트 영상 해시가 작업 전후 동일하다. 손상·잠금·읽기 전용·기본 앱 실패 경로가 데이터를 보존한다. | 실행 앱, 임시 영상·앱 데이터 검증 기록 | 검증 완료 | 격리된 검증 데이터로 파일 목록·SHA-256 전후 비교 및 실패 경로 수동 확인 | 통과 — 앱 파일은 `library.json`, `posters/manual.png`, 의도한 `library.corrupt-*.json`뿐; MP4 3개 SHA-256 모두 시작값 `0CD83D…24451`, `.offline` 없음; 손상·잠금·저장·기본 앱 실패에서 기존 데이터 유지 | 만족 |  |
| IMP-027 | 필수 | 레이아웃·성능 수동 검증 | Task 9 Step 3 | 1440×1000 및 최소 폭에서 레퍼런스 구조·색·간격·2:3·줄바꿈을 만족하고, 1,000개 고해상도 포스터 라이브러리의 표시·스크롤·검색·정렬을 완료한다. | 실행 앱 UI | 검증 완료 | 브라우저 레퍼런스 병렬 비교와 1,000개 fixture 수동 실행 | 통과 — 1440×1000·760×640에서 구조/색/비율/열 변화와 가로 잘림 없음; 1,000개 7.34초 표시, 검색 0.16초, 정렬 2.11초, 하단 스크롤·방향키 자동 스크롤 완료, UI 응답 유지 | 만족 |  |
| IMP-028 | 필수 | 키보드·팝업·잠금·편집 수동 검증 | Task 9 Step 4~5 | 계획서의 키보드, 선택, 팝업 우선순위, 탐색/저장 잠금, 실제 duration, 메타데이터 저장 재시도 체크리스트가 모두 재현된다. | 실행 앱 UI와 실제 지원 영상 | 검증 완료 | Task 9 Step 4~5 실제 UI와 차단 delegate 자동 테스트 기록 | 통과 — 검색/선택/정렬/팝업 우선순위/F1/Enter/Esc/세 재생 경로/경고 원인/메타데이터 성공·실패/실제 duration을 UI에서 확인; 순간적인 스캔·저장 잠금은 차단 delegate 테스트로 상태 전 구간과 커밋 1회를 재현 | 만족 |  |
| IMP-029 | 필수 | 그래프 최신화 | 프로젝트 `AGENTS.md` | 코드 변경 뒤 지식 그래프를 갱신한다. | `graphify-out/` | 검증 완료 | `graphify update .`; `graphify query ...` | 통과 — 최종 코드 기준 459 nodes, 847 edges, 30 communities로 재생성하고 MainWindow↔MainViewModel↔LibraryStore 관계 조회 성공 | 만족 |  |
| IMP-030 | 필수 | 전역 구현·언어·인코딩 제약 | 전역 제약, 파일 구조 설명, 의도적으로 생략한 것 | 신규 NuGet 런타임 의존성, DI 컨테이너, UI 프레임워크, 데이터베이스, 웹 글꼴을 추가하지 않는다. 단일 구현체인 저장소·Windows 실행기에 인터페이스를 만들지 않는다. 사용자 문자열과 사용자 대상 문서는 한국어이며 생성 텍스트 파일은 UTF-8 BOM 없음이다. | 전체 프로젝트·프로젝트 파일·생성 문서와 소스 | 검증 완료 | `dotnet list ... package`; `rg` 금지 의존성; 텍스트 BOM 바이트 검사 | 통과 — 앱 런타임 패키지 0, 테스트 패키지는 MSTest 2개뿐, 금지 프레임워크·추상화 없음, 검사 텍스트 38개 중 UTF-8 BOM 0 | 만족 |  |

## 구현 가정

- 구현 계획서의 코드와 테스트 계약을 우선하며, 컴파일 또는 명시 수용 기준 충족에 꼭 필요한 최소 보정만 한다.
- 저장소에는 시작 시 앱 코드가 없으므로 기존 코드 재사용 대상은 없고, 사용자 제공 스펙과 레퍼런스 디자인을 입력으로 사용한다.
- 사용자가 요청한 브랜치는 `feat/dabom-video-manager`로 생성했으며, 기존 untracked 문서와 레퍼런스는 현재 checkout에서 그대로 보존한다.
- 계획서가 실행 대상으로 명시한 `reference-design/verify.ps1`이 누락되어 있다. 기존 세 레퍼런스 파일의 시작 SHA-256을 고정해 변경 여부만 확인하는 최소 스크립트를 새로 만든다. 레퍼런스 내용을 수정하거나 새로운 시각 요구를 만들지 않는다.
- 자동화할 수 없는 Windows 기본 앱, 실제 Property System 재생시간, GUI·고부하 수동 항목은 실행 가능한 fixture와 도구가 확보되는 범위에서 직접 검증하고, 확인하지 못한 필수 항목을 완료로 숨기지 않는다.

## 변경 기준점

- 저장소 루트: `C:\Users\tatis\Repos\Dabom`
- 작업 브랜치: `feat/dabom-video-manager`
- 시작 커밋: `c1c34b9c884026454e0bd111cd4128f2f191a0a9`
- 시작 staged 상태: 없음
- 시작 unstaged 상태: 없음
- 시작 untracked 상태:
  - `.gitignore`
  - `docs/superpowers/plans/2026-07-18-dabom-video-manager.md`
  - `docs/superpowers/specs/2026-07-18-dabom-video-manager-design.md`
  - `reference-design/app.js`
  - `reference-design/index.html`
  - `reference-design/style.css`
- 이번 루프가 생성하거나 수정한 경로:
  - `.gitignore`
  - `docs/implementation/2026-07-18-dabom-video-manager-implementation-map.md`
  - `Dabom.sln`
  - `graphify-out/` (프로젝트 규칙에 따른 로컬 생성 그래프; 시작 시 미존재)
  - `reference-design/verify.ps1`
  - `src/Dabom/`
  - `tests/Dabom.Tests/`
- 이번 루프 중 생성한 커밋:
  - `1d35600 feat: add Dabom domain foundation`
  - `51e8da9 feat: scan local video libraries`
  - `39e4651 feat: persist library data safely`
  - `1ec4646 feat: coordinate library state`
  - `49e1620 feat: edit metadata and track playback`
  - `6cbad23 feat: gate library commands during scans`
  - `a268982 feat: add the main library experience`
  - `e55cba5 feat: add metadata editing window`
  - `c6d3eca fix: complete Dabom MVP verification`
  - `2e74458 fix: make warning lists scrollable`
- 레퍼런스 시작 SHA-256:
  - `reference-design/index.html`: `8F8F147A5095F879ADC0899E4D858C193425E33AA122012187A289F9C995A8D5`
  - `reference-design/style.css`: `6113FBAC86FBE7A15DFF6E187C20ED2EB064926C0B4DCF90F7BFD69452109FB8`
  - `reference-design/app.js`: `5BF01813754CE3ADD0ADE18AA1653D41C8F9F084EE85A56B4BA1A87C491FFE0E`

## 보류 항목

- 없음.
- Windows 전체 설정인 `클라이언트 영역 애니메이션`은 사용자 환경을 변경하지 않았다. 비활성 분기는 `SystemParameters.ClientAreaAnimation == false`일 때 두 애니메이션 시작을 모두 건너뛰는 코드 경로와 빌드로 확인했다.
- 실제 스캔은 3개 파일에서 너무 빨리 끝나 진행 문구를 화면 캡처하지 못했다. 진행 중 양쪽 표시·명령 잠금의 전체 생명주기는 완료를 제어하는 `BlockingScanner`/저장 delegate 자동 테스트로 재현했다.

## 구현 계획서 모순

- `reference-design/verify.ps1`을 두 차례 실행하도록 명시했지만 시작 저장소에는 없다. IMP-024의 최소 SHA-256 보존 스크립트로 채우는 해석을 검증관에게 사전 검토 요청한다.
- 계획서는 레퍼런스 보존 색으로 `#0E0F10`, `#171819`, `#CBC9C2`를 명시하므로, 레퍼런스 CSS의 다른 기본 디자인 토큰보다 `body[data-design="c"]` 토큰과 계획서 Task 7 값을 우선한다.

## 검증 요약

- 시작 환경 확인: .NET SDK `9.0.316`, Windows Desktop Runtime `9.0.18` 설치 확인.
- 시작 코드와 솔루션이 없어 baseline 빌드·테스트는 해당 없음.
- 구현 전 매핑 검토: 검증관 `만족`.
- Task 1: `LibraryRulesTests` RED 확인 후 4/4 통과, Debug 전체 빌드 경고 0·오류 0.
- Task 2: 스캐너/Windows duration RED 확인 후 관련 테스트 6/6 통과. Task 9에서 실제 MP4 duration도 확인했다.
- Task 3: 저장소/포스터 RED 확인 후 관련 10/10, 전체 20/20 통과.
- Task 4: `Dabom.Main` RED 확인 후 MainViewModelTests 9/9, 전체 29/29 통과.
- Task 5: `Dabom.Metadata` RED 확인 후 재생·메타데이터 관련 19/19, 전체 39/39 통과.
- Task 6: 명령 API RED 확인 후 CommandStateTests 5/5, 전체 44/44, Debug 빌드 경고 0·오류 0 통과.
- Task 7: 초기화 RED 확인 후 2/2, 전체 46/46, XAML 포함 Debug 빌드 경고 0·오류 0, 레퍼런스 보존 1/1 통과. Task 9에서 실제 GUI까지 완료했다.
- Task 8: 손상 포스터 미리보기 보존을 포함한 MetadataEditorViewModelTests 4/4, 전체 47/47, 두 창 Debug 빌드 경고 0·오류 0 통과. Task 9에서 성공·실패 모달을 실제 확인했다.
- Task 9 실행 중 실제 WPF 시작에서 읽기 전용 `Run.Text`의 기본 TwoWay 바인딩 예외, 파생 Window의 암시적 스타일 미적용, 잘못된 `Thickness`, 다크 ComboBox/카드 전경, Popup HWND의 인접 카드 hit-test 차단을 재현하고 원인별 최소 수정했다.
- 경고 버튼 접근성 이름 회귀 테스트는 수정 전 1/1 실패, 수정 후 1/1 통과했고 실제 UI Automation 이름 `경고 2건`을 확인했다.
- 최종 자동 검증: `dotnet build Dabom.sln -c Debug --no-restore` 경고 0·오류 0, `dotnet test Dabom.sln -c Debug --no-build --no-restore` 49/49, `reference-design/verify.ps1` 1/1.
- 실제 MP4 3개를 사용해 Windows duration `50,550,000 ticks`, 일반 더블클릭·Enter·Featured 기본 앱 실행, 성공 이력 저장과 실패 이력 불변을 확인했다.
- 빈 상태, 정상 3개, 손상 JSON, 잠긴 JSON, 없는 포스터, 연결 해제·권한 거부 위치, 메타데이터 성공·저장 실패를 실제 앱에서 확인했다. 메타데이터 실패 때 창·초안·미리보기·JSON이 유지됐다.
- 브라우저에서 `reference-design/index.html`을 열어 실제 1440×1000 WPF 화면과 구조·색·비율을 비교했다. 760×640 최소 창에서도 가로 잘림이 없었다.
- 1,000개 fixture는 7.34초 표시, 검색 0.16초, 개봉일 정렬 2.11초였고 UI 응답을 유지했다. 하단 상태 영역과 방향키의 화면 밖 카드 자동 스크롤을 확인했다.
- 검증 영상 3개의 최종 SHA-256은 모두 시작값 `0CD83D944A6CA7822B4A8306CECC60A36E859B041F6702C6A1AD9EAD78924451`이며 임시 `.offline` 파일은 없다.
- `%LocalAppData%\Dabom`의 앱 산출물은 `library.json`, `posters/manual.png`, 의도한 손상 복구 백업 1개뿐이다. 최종 `library.json`은 정상 위치 1개와 영상 3개 fixture로 복원했다.
- 패키지 감사에서 앱 런타임 NuGet 참조 0, 테스트 전용 MSTest 참조 2개를 확인했다. 검사 텍스트 38개에서 UTF-8 BOM은 0개다.
- `graphify update .` 최종 결과 459 nodes, 847 edges, 30 communities이며 `graphify query`로 주요 계층 관계를 조회했다.
- MyLoop 구현 결과 검토 1차는 IMP-021의 긴 경고 목록이 세로 `StackPanel`에서 잘릴 수 있다는 Important 1건으로 `불만족`이었다. 420px 초과 회귀 테스트를 RED로 확인한 뒤 헤더/목록을 `Auto,*` Grid로 바꾸고 세로 스크롤을 명시했다. 실제 경고 30개에서 목록 높이 341px, `VerticallyScrollable=True`, 위치 0→100, 마지막 경로·원인 표시를 확인했다.

## 남은 리스크

- 앱 시작 전 `%LocalAppData%\Dabom` 폴더가 없음을 확인했으므로 이번 검증 데이터가 기존 사용자 데이터를 덮어쓰지 않았다. 현재 남아 있는 정상 fixture와 의도한 손상 백업은 필요하면 사용자가 직접 제거할 수 있다.
- 1,000개 검증은 계획이 허용한 단순 `WrapPanel`로 통과했지만, 훨씬 큰 라이브러리는 별도 성능 요구가 생길 때만 가상화 도입을 재평가한다.
