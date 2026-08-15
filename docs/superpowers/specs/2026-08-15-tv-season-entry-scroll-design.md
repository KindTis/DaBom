# TV 시즌 상세 진입 스크롤 설계

## 목표

- 전체 목록에서 TV 시즌 카드를 열면 시즌 상세 화면의 `MainScrollViewer`를 최상단에서 보여 준다.
- 시즌 상세 화면에서 전체 목록으로 돌아오면 진입 전에 저장한 스크롤 위치와 시즌 카드 포커스를 기존처럼 복원한다.

## 변경 설계

`MainWindow.OpenSeason`은 진입 직전에 현재 `MainScrollViewer.VerticalOffset`을 `_seasonReturnOffset`에 저장한다. `MainViewModel.OpenSeason`이 성공한 경우에만 `MainScrollViewer.ScrollToTop()`을 호출해 시즌 상세 화면의 시작 위치를 초기화한다.

복귀는 기존 `RestoreSeasonReturn`이 `_seasonReturnOffset`을 `ScrollToVerticalOffset`으로 복원하는 흐름을 그대로 사용한다. 뷰 모델 상태, 저장 형식, 시즌 그룹 계산에는 변경을 추가하지 않는다.

## 오류와 경계 조건

- 시즌 열기가 실패하면 스크롤 위치를 변경하지 않는다.
- 시즌 상세에서 검색·필터·정렬을 바꾸더라도 전체 목록 복귀 시 기존 복원 규칙을 유지한다.
- 복귀할 시즌 카드가 사라진 경우의 목록 포커스 처리도 기존 동작을 유지한다.

## 검증

기존 비표시 `MainWindowMarkupTests`에 다음을 확인하는 회귀 검사를 추가한다.

1. `OpenSeason` 성공 경로가 `MainScrollViewer.ScrollToTop()`을 호출한다.
2. `RestoreSeasonReturn`은 계속 `_seasonReturnOffset`을 복원한다.

해당 테스트를 먼저 실패시키고, 최소 구현 후 같은 테스트가 통과하는지 확인한다.
