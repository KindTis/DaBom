# DABOM 자동 메타데이터 적용 설계

- 작성일: 2026-07-25
- 기준 문서: `docs/references/MovieMeta.md`
- 테스트 영상 위치: `E:\MyVideo`
- 최초 메타데이터 공급자: TMDB

## 1. 목적

DABOM이 라이브러리를 검색한 뒤 영상 파일명에서 영화 또는 TV 에피소드 정보를 추출하고, 외부 메타데이터를 자동으로 조회하여 라이브러리에 적용한다.

첫 구현은 TMDB만 지원하지만, 사용하는 쪽에서는 구체적인 공급자를 알지 못하고 `IMetadataProvider`만 사용한다. 이후 공급자는 같은 인터페이스 구현과 등록 순서 추가만으로 연결할 수 있어야 한다.

자동화가 기본 동작이다. 사용자가 검색 후보를 고르는 과정은 제공하지 않는다.

## 2. 범위

### 포함

- 영화와 TV 에피소드 파일명 분석
- 라이브러리 검색 후 자동 메타데이터 조회
- 등록된 공급자의 순차 검색
- 검색 결과의 첫 번째 후보 자동 적용
- TMDB 영화, TV 시리즈, 에피소드 정보 조회
- 한국어 우선 메타데이터와 영어 보완 조회
- 포스터 다운로드 및 로컬 저장
- 공급자 중립적인 검색 상태와 외부 참조 저장
- 향후 사용자 편집을 위한 `UserEditedFields` 상태 정의
- 진행 상황과 결과 개수 표시
- 상세 영역의 장르 표시
- `ABOUT` 다이얼로그를 통한 앱 소개와 TMDB 고지
- 단위 테스트와 `E:\MyVideo` 수동 통합 검증

### 제외

- 검색 후보 선택 UI
- 오매칭 수정 및 외부 연결 해제를 수행하는 전용 편집창
- 둘 이상의 공급자 결과 병합
- 신뢰도 점수 또는 후보 재정렬
- 백드롭, 제작사, 국가, 콘텐츠 등급, 이미지 갤러리
- 병렬 또는 일괄 메타데이터 요청
- 설정 화면을 통한 TMDB 토큰 관리
- `README.md`의 TMDB 크레딧 또는 기타 변경
- `.env.example`을 포함한 예제 자격 증명 파일

## 3. 핵심 결정

1. 새 영상은 라이브러리 검색 완료 후 자동으로 메타데이터 조회 대상이 된다.
2. 한 공급자가 하나 이상의 검색 결과를 반환하면 첫 번째 후보를 사용한다.
3. 공급자는 등록 순서대로 조회하며, 완전한 메타데이터를 먼저 반환한 공급자가 선택된다.
4. 모든 공급자가 정상적으로 검색했지만 결과가 없을 때만 `NotFound`가 된다.
5. `NotFound`는 다음 라이브러리 검색에서 자동 재조회하지 않는다.
6. 일시적인 오류는 `Failed`로 저장하고 다음 라이브러리 검색에서 재시도한다.
7. 자동 검색 여부는 외부 ID의 존재가 아니라 `MetadataStatus`로 판단한다.
8. TMDB 포스터 URL은 영구 저장하지 않는다. 파일을 로컬로 내려받고 상대 경로만 저장한다.
9. TMDB 고지는 `ABOUT` 다이얼로그에만 표시한다.

## 4. 구성 요소

### `MediaFilenameParser`

영상 경로를 받아 공급자에 독립적인 `MetadataQuery`를 만든다.

- 네트워크와 저장소에 의존하지 않는 순수 로직이다.
- 영화 제목, 개봉 연도, 미디어 유형을 추출한다.
- 에피소드라면 시리즈 제목, 시즌 번호, 에피소드 번호를 추출한다.
- 공급자 이름이나 TMDB ID를 알지 못한다.

### `IMetadataProvider`

메타데이터 사용 측이 의존하는 유일한 공급자 계약이다.

```csharp
public interface IMetadataProvider
{
    string ProviderKey { get; }

    Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        MetadataQuery query,
        CancellationToken cancellationToken);

    Task<MetadataDetails> GetDetailsAsync(
        MetadataCandidate candidate,
        CancellationToken cancellationToken);
}
```

`MetadataQuery`, `MetadataCandidate`, `MetadataDetails`는 공급자 중립적인 형식이다. 공급자별 HTTP 응답 DTO는 해당 공급자 내부에서만 사용한다.

### `TmdbMetadataProvider`

- TMDB 인증과 HTTP 요청을 담당한다.
- 영화와 TV 검색 결과를 공통 후보 형식으로 변환한다.
- 선택된 후보의 상세 정보와 크레딧을 공통 상세 형식으로 변환한다.
- TMDB 이미지 설정으로 임시 포스터 URI를 만든다.
- TMDB DTO나 ID를 UI와 조정 서비스에 노출하지 않는다.

### `MetadataEnrichmentService`

- 저장된 상태를 보고 자동 조회 대상을 결정한다.
- 파일명 분석기를 호출한다.
- `IReadOnlyList<IMetadataProvider>`를 등록 순서대로 호출한다.
- 첫 번째 후보 선택, 오류 분류, 상태 전이, 포스터 저장을 조정한다.
- 구체적인 공급자 형식을 해석하지 않는다.

### `LibraryStore`

- 확장된 `VideoRecord`를 기존 `library.json`에 원자적으로 저장한다.
- 내려받은 포스터를 기존 포스터 디렉터리에 저장한다.
- JSON 저장 실패 시 이번 작업에서 새로 만든 포스터를 제거한다.

### 조립 위치

`App.xaml.cs`에서 `TmdbMetadataProvider`를 만들고 `IMetadataProvider` 목록에 등록한다. 구체적인 공급자를 아는 위치는 이 조립 지점과 공급자 자체로 제한한다. 별도의 DI 컨테이너, 공급자 라우터, 결과 병합기는 만들지 않는다.

## 5. 데이터 모델

### 미디어 유형

```text
Unknown
Movie
TvEpisode
```

### 메타데이터 상태

```text
Unspecified = 0
Pending
Matched
NotFound
Failed
Manual
```

| 상태 | 의미 | 다음 자동 검색 대상 |
|---|---|---|
| `Unspecified` | 이전 JSON을 읽기 위한 마이그레이션 표식 | 정규화 후 다른 상태로 변경 |
| `Pending` | 아직 자동 검색하지 않음 | 예 |
| `Matched` | 공급자 메타데이터 적용 완료 | 아니요 |
| `NotFound` | 모든 공급자가 정상 응답했지만 검색 결과가 없음 | 아니요 |
| `Failed` | 인증, 통신, 응답 처리 또는 포스터 저장 실패 | 예 |
| `Manual` | 사용자가 외부 연결을 해제하고 직접 관리 | 아니요 |

`NotFound`를 명시적으로 저장하므로 같은 파일이 라이브러리 검색 때마다 반복 조회되지 않는다. 향후 전용 편집창에서 다시 검색을 요청할 때만 `Pending`으로 되돌린다.

### `ProviderReference`

```text
ProviderKey   공급자 식별자. 예: "tmdb"
ResourceType 공급자가 해석하는 자원 유형. 예: "movie", "tv-series", "tv-episode"
ResourceId   공급자 내부 자원 ID
```

외부 참조는 ID 하나만 저장하지 않고 공급자와 자원 유형을 함께 저장한다. 같은 숫자 ID가 다른 공급자나 자원 유형에서 사용되어도 잘못 해석되지 않는다.

- 영화는 영화 참조 하나를 가진다.
- TV 에피소드는 시리즈 참조와 에피소드 참조를 가진다.
- 조회 가능 여부는 이 목록이 아니라 `MetadataStatus`로 판단한다.
- 일반 사용 코드는 참조 내용을 해석하지 않는다. 같은 `ProviderKey`의 공급자만 참조를 해석한다.

### `UserEditedFields`

향후 전용 편집창이 사용자가 직접 바꾼 필드를 기록할 수 있도록 `VideoRecord`에 필드 식별자 집합을 둔다. JSON에는 안정적인 문자열 이름으로 저장한다.

대상 필드 식별자는 다음과 같다.

```text
Title
OriginalTitle
SeriesTitle
EpisodeTitle
ReleaseDate
Genres
Director
Actors
Synopsis
Poster
MediaType
SeasonNumber
EpisodeNumber
```

이번 자동화 구현에서는 새 매칭의 `UserEditedFields`를 빈 집합으로 초기화하며, 전용 편집창과 편집 동작은 구현하지 않는다. 자동 재시도나 향후 새로 고침은 이 집합에 든 필드를 덮어쓰지 않아야 한다.

### `VideoRecord` 추가 정보

기존 필드에 다음 정보를 추가한다.

- 미디어 유형
- 시리즈 제목
- 에피소드 제목
- 시즌 번호
- 에피소드 번호
- 장르 목록
- 메타데이터 상태
- 공급자 참조 목록
- 사용자 편집 필드 목록

`Poster`의 의미는 계속 로컬 포스터 상대 경로다.

## 6. 기존 데이터 마이그레이션

이전 `library.json`에는 상태 필드가 없으므로 역직렬화하면 `Unspecified`가 된다. 로드 정규화 과정에서 다음과 같이 한 번 변환한다.

1. 기존 메타데이터 필드에 사용자가 저장한 값이 있으면 `Manual`
2. 그 외에는 `Pending`

사용자 저장 값은 다음 중 하나로 판단한다.

- 원제, 개봉일, 감독, 배우, 줄거리, 포스터 중 하나라도 값이 있음
- 제목이 영상 경로의 확장자를 제외한 파일명과 다름

제목이 파일명과 같고 다른 메타데이터가 없는 기존 레코드만 `Pending`으로 본다. 따라서 제목만 직접 수정한 기존 레코드도 `Manual`로 보존한다.

정규화 후 `Unspecified`는 다시 저장하지 않는다.

## 7. 파일명 분석 규칙

1. 확장자를 제거한다.
2. 점과 밑줄을 공백 구분자로 정리한다.
3. 영화보다 에피소드 형식을 먼저 찾는다.
4. `SxxEyy`가 있으면 앞부분만 시리즈 검색어로 사용한다.
5. `Eyy`만 있으면 시즌 1로 해석한다.
6. 영화의 연도 후보가 여러 개면 릴리스 태그 앞의 가장 오른쪽 네 자리 연도를 개봉 연도로 사용한다.
7. 해상도, 코덱, 소스, 오디오, 릴리스 그룹 같은 태그를 제목에서 제거한다.
8. 숫자 `007`, 소수 형태 `3.33`, 날짜 형태 `161209`를 개봉 연도로 오인하지 않는다.
9. 에피소드 표식 뒤의 챕터 또는 에피소드 제목은 시리즈 검색어에 포함하지 않는다.

분석 결과가 불완전해도 공급자 검색이 가능한 최소 제목이 있으면 조회한다. 제목을 만들 수 없으면 해당 항목을 `Failed`로 저장한다.

## 8. 자동 처리 흐름

1. 기존 `LibraryScanner`가 영상 파일을 찾고 레코드를 갱신한다.
2. 새 레코드는 파일명을 제목으로 유지한 채 `Pending`으로 생성한다.
3. 저장된 상태가 `Pending` 또는 `Failed`인 항목만 자동 처리한다.
4. `Matched`, `NotFound`, `Manual`은 건너뛴다.
5. 파일명에서 공급자 중립 검색어를 만든다.
6. 공급자를 등록 순서대로 하나씩 호출한다.
7. 검색 결과가 없으면 다음 공급자로 이동한다.
8. 검색 또는 상세 조회가 실패하면 다음 공급자로 이동한다.
9. 검색 결과가 있으면 첫 후보의 상세 정보를 조회한다.
10. 상세 정보가 완성되면 해당 공급자를 선택하고 이후 공급자는 호출하지 않는다.
11. 포스터가 있으면 내려받아 로컬에 저장한다.
12. 완성된 레코드를 원자적으로 저장한 뒤 메모리와 UI에 반영한다.
13. 한 항목이 끝난 뒤 다음 항목을 처리한다.

여러 공급자의 검색이 모두 정상 완료되고 결과가 하나도 없으면 기존 파일명 표시를 그대로 둔 채 `NotFound`만 저장한다. 하나 이상의 공급자 오류가 있었고 어느 공급자도 상세 정보를 반환하지 못했다면 `Failed`로 저장한다.

상세 정보가 완성됐다는 것은 영화의 경우 표시 제목과 영화 참조가 있고, TV 에피소드의 경우 시리즈 표시 제목, 시즌·에피소드 번호, 시리즈·에피소드 참조가 있다는 뜻이다. 그 밖의 메타데이터 필드와 포스터는 없어도 정상 결과로 인정한다.

## 9. TMDB 조회

### 인증

저장소 루트의 `.env`에서 다음 값만 읽는다.

```text
DABOM_TMDB_ACCESS_TOKEN=<TMDB API Read Access Token>
```

- 토큰은 `Authorization: Bearer` 헤더로 전달한다.
- `.env`는 Git 추적에서 제외한다.
- `.env.example`은 만들지 않는다.
- 토큰과 인증 헤더는 로그나 오류 메시지에 출력하지 않는다.
- 토큰을 읽는 별도 설정 UI는 만들지 않는다.

TMDB 인증 방식은 [TMDB Application Authentication](https://developer.themoviedb.org/docs/authentication-application)을 따른다.

### 영화

1. [`/search/movie`](https://developer.themoviedb.org/reference/search-movie)로 제목과 연도를 검색한다.
2. 첫 후보를 선택한다.
3. [영화 상세 정보](https://developer.themoviedb.org/reference/movie-details)를 조회한다.
4. [영화 크레딧](https://developer.themoviedb.org/reference/movie-credits)을 조회한다.

### TV 에피소드

1. [`/search/tv`](https://developer.themoviedb.org/reference/search-tv)로 시리즈 제목을 검색한다.
2. 첫 시리즈 후보를 선택한다.
3. 시리즈 상세 정보와 [시리즈 크레딧](https://developer.themoviedb.org/reference/tv-series-credits)을 조회한다.
4. 파싱한 시즌과 에피소드 번호로 [에피소드 상세 정보](https://developer.themoviedb.org/reference/tv-episode-details)를 조회한다.
5. [에피소드 크레딧](https://developer.themoviedb.org/reference/tv-episode-credits)을 조회한다.

### 언어 보완

- 최초 상세 요청은 `ko-KR`로 한다.
- 제목이나 줄거리가 비어 있을 때만 `en-US` 상세 정보를 추가 조회한다.
- 영어 응답은 비어 있는 필드만 채우며 한국어 값을 덮어쓰지 않는다.

## 10. 메타데이터 매핑

### 영화

| DABOM 필드 | TMDB 값 |
|---|---|
| 제목 | 한국어 표시 제목 |
| 원제 | 원제 |
| 개봉일 | 개봉일 |
| 장르 | 상세 정보의 장르 |
| 줄거리 | 한국어 우선 줄거리 |
| 감독 | 크레딧에서 직무가 Director인 첫 인물 |
| 배우 | TMDB 순서의 상위 10명 |
| 포스터 | `poster_path`로 내려받은 로컬 파일 |
| 외부 참조 | `tmdb` 영화 ID |

### TV 에피소드

| DABOM 필드 | TMDB 값 |
|---|---|
| 시리즈 제목 | 한국어 시리즈 제목 |
| 원제 | 시리즈 원제 |
| 에피소드 제목 | 한국어 에피소드 제목 |
| 방영일 | 에피소드 방영일 |
| 장르 | 시리즈 장르 |
| 줄거리 | 에피소드 줄거리 |
| 감독 | 에피소드 크레딧의 감독 |
| 배우 | 에피소드 출연진과 게스트 중 TMDB 순서의 상위 10명 |
| 포스터 | 시리즈 `poster_path`로 내려받은 로컬 파일 |
| 외부 참조 | `tmdb` 시리즈 ID와 에피소드 ID |

TV 에피소드 표시 제목은 다음 형식을 사용한다.

```text
{시리즈 제목} S{시즌 2자리}E{에피소드 2자리} · {에피소드 제목}
```

예:

```text
더 만달로리안 S02E01 · 에피소드 제목
도깨비 S01E04 · 에피소드 제목
```

에피소드 제목이 없으면 가운데점과 뒤쪽 제목을 생략한다.

## 11. 포스터 처리

TMDB의 `poster_path`는 완전한 웹 URL이 아니다. [TMDB 이미지 기본 안내](https://developer.themoviedb.org/docs/image-basics)에 따라 이미지 설정의 기반 URL과 `w500` 크기를 조합하여 다운로드 URI를 만든다.

처리 순서는 다음과 같다.

1. TMDB 상세 응답에서 `/abc.jpg` 형태의 `poster_path`를 받는다.
2. 실행 중에만 완전한 다운로드 URI를 만든다.
3. `%LocalAppData%\Dabom\posters\<GUID>.jpg`에 저장한다.
4. `library.json`에는 `posters/<GUID>.jpg`만 저장한다.
5. 원격 URL은 저장하지 않는다.

포스터가 없는 메타데이터는 정상 매칭으로 처리하며 `Poster`는 비워 둔다. 포스터 다운로드가 실패하면 텍스트 메타데이터와 공급자 참조는 저장하되 상태를 `Failed`로 두어 다음 라이브러리 검색에서 재시도한다.

새 포스터 저장 후 JSON 저장이 실패하면 새 포스터를 삭제하고 이전 레코드와 이전 포스터를 유지한다.

## 12. 사용자 편집 시나리오

이번 범위에서는 아래 상태만 정의하고 전용 편집창은 구현하지 않는다.

### 올바른 매칭 후 사용자가 정보를 보충하는 경우

- 상태는 `Matched`를 유지한다.
- 공급자 참조를 유지한다.
- 사용자가 바꾼 필드만 `UserEditedFields`에 추가한다.
- 향후 새로 고침은 표시된 필드를 덮어쓰지 않는다.

### 잘못된 매칭 후 사용자가 직접 정보를 입력하는 경우

일반 필드 편집만으로 외부 매칭이 틀렸다고 추론하지 않는다. 향후 전용 편집창에서 명시적인 동작을 제공한다.

- **외부 매칭 해제:** 상태를 `Manual`로 바꾸고 공급자 참조를 비운다.
- **다른 항목 선택:** 새 공급자 참조로 교체하고 상태를 `Matched`로 유지한다.

현재 구현에는 두 동작의 UI와 처리 로직을 포함하지 않는다.

## 13. 오류 처리

| 상황 | 처리 |
|---|---|
| 모든 공급자가 정상 응답하고 검색 결과가 0개 | 파일명 표시 유지, `NotFound` |
| 네트워크 오류, 시간 초과, HTTP 429, 5xx | 최대 3회 시도 후 `Failed` |
| `Retry-After`가 있는 HTTP 429 | 해당 값을 우선 적용 |
| 토큰 누락, HTTP 401 또는 403 | 같은 검색 중 재시도하지 않고 `Failed` |
| 응답 역직렬화 또는 필수 값 처리 실패 | 같은 검색 중 재시도하지 않고 `Failed` |
| 포스터 경로 없음 | 포스터 없이 `Matched` |
| 포스터 다운로드 실패 | 텍스트와 참조 저장, `Failed` |
| 한 영상 처리 실패 | 다른 영상 처리는 계속 진행 |

재시도 횟수 3회는 최초 요청을 포함한다. 공급자가 추가되면 한 공급자의 검색 또는 상세 조회 실패는 다음 공급자로 넘어간다.

## 14. UI

### 자동 적용 결과

- 기존 제목, 원제, 개봉일, 감독, 배우, 줄거리, 포스터 영역은 저장 성공 후 갱신한다.
- 기존 상세 영역에 장르 한 줄을 추가한다.
- 상태 영역에 처리 중 항목과 성공, 검색 결과 없음, 실패 개수를 표시한다.
- 검색 후보 선택 화면은 만들지 않는다.

### `ABOUT` 버튼과 다이얼로그

- 좌측 상단의 `DABOM` 타이틀 바로 오른쪽에 `ABOUT` 버튼을 배치한다.
- 버튼을 누르면 소유 창 중앙에 모달 다이얼로그를 연다.
- 다이얼로그에는 다음 내용을 표시한다.
  - 앱 이름: `DABOM`
  - 앱 소개: `지정한 보관 위치의 영화, 드라마, 애니메이션을 찾아 메타데이터와 포스터를 함께 관리하는 Windows 데스크톱 앱입니다.`
  - 실행 중인 어셈블리에서 읽은 앱 버전
  - TMDB가 제공하는 승인 로고
  - [TMDB 웹사이트](https://www.themoviedb.org/) 링크
  - 다음 고지문 원문

> This product uses the TMDB API but is not endorsed or certified by TMDB.

- `닫기` 버튼과 `Esc` 키로 닫을 수 있어야 한다.
- 키보드로 `ABOUT` 버튼과 `닫기` 버튼에 접근할 수 있어야 한다.
- TMDB 로고는 앱 로고보다 덜 두드러지게 표시하고 TMDB의 보증을 암시하지 않는다.
- TMDB 고지는 `README.md`에 중복 추가하지 않는다.

고지 방식과 승인 로고는 [TMDB Attribution FAQ](https://developer.themoviedb.org/docs/faq) 및 [TMDB Logos & Attribution](https://www.themoviedb.org/about/logos-attribution)을 따른다. 상업적 사용으로 전환할 때는 TMDB의 별도 사용 조건을 다시 확인해야 한다.

## 15. 테스트

### 파일명 분석 단위 테스트

`E:\MyVideo`의 다음 8개 파일명을 고정 테스트 사례로 사용한다.

| 파일명 | 기대 유형 | 기대 검색 정보 |
|---|---|---|
| `007.No.Time.to.Die.2021.2160p.WEB-DL.DDP5.1.Atmos.HDR.HEVC-NOTIMETOCRY.mp4` | 영화 | `007 No Time to Die`, 2021 |
| `1917.2019.2160p.UHD.BluRay.x265.10bit.HDR.DTS-HD.MA.TrueHD.7.1.Atmos-SWTYBLZ.mp4` | 영화 | `1917`, 2019 |
| `도깨비.E04.161209.720p-NEXT.mp4` | TV 에피소드 | `도깨비`, S01E04 |
| `도깨비.E05.161216.720p-NEXT.mp4` | TV 에피소드 | `도깨비`, S01E05 |
| `Evangelion.3.33.You.Can.(Not).Redo.2012.1080p.BluRay.x264-CHD.mp4` | 영화 | `Evangelion 3.33 You Can (Not) Redo`, 2012 |
| `John.Wick.Chapter.4.2023.2160p.WEB-DL.DDP5.1.Atmos.DV.HDR10.h265-CMRG.mp4` | 영화 | `John Wick Chapter 4`, 2023 |
| `The.Mandalorian.S02E01.Chapter.16.The.Rescue.2160p.WEB-DL.DDP5.1.Atmos.HDR.x265-MZABI.mkv` | TV 에피소드 | `The Mandalorian`, S02E01 |
| `The.Mandalorian.S02E02.Chapter.16.The.Rescue.2160p.WEB-DL.DDP5.1.Atmos.HDR.x265-MZABI.mkv` | TV 에피소드 | `The Mandalorian`, S02E02 |

샘플 파일은 0바이트이므로 컨테이너 정보나 재생 시간을 검증하지 않는다. 파일명 분석과 외부 메타데이터 적용만 검증한다.

### 조정 서비스 테스트

가짜 `IMetadataProvider`로 다음 동작을 검증한다.

- 첫 번째 후보가 적용된다.
- 첫 번째 공급자의 첫 후보 상세 조회가 성공하면 뒤 공급자를 호출하지 않는다.
- 앞 공급자의 검색 또는 첫 후보 상세 조회가 실패하거나 검색 결과가 없으면 다음 공급자를 호출한다.
- 모든 정상 검색 결과가 없으면 `NotFound`가 된다.
- 오류 후 결과를 얻지 못하면 `Failed`가 된다.
- `Failed`는 다음 라이브러리 검색에서 재시도한다.
- `Matched`, `NotFound`, `Manual`은 자동 검색하지 않는다.
- `UserEditedFields`의 값은 덮어쓰지 않는다.

### TMDB 공급자 테스트

가짜 `HttpMessageHandler`를 사용해 요청과 응답 매핑을 검증한다.

- 영화와 TV 검색 경로
- Bearer 인증 헤더
- 한국어 우선 및 영어 빈 필드 보완
- 감독과 상위 10명 배우 매핑
- 시리즈와 에피소드 참조 생성
- 429, 5xx, 인증 실패, 잘못된 응답 처리
- 토큰이 로그나 예외 메시지에 포함되지 않음

자동화 테스트는 실제 TMDB API를 호출하지 않는다.

### 저장 테스트

- 새 데이터 모델의 JSON 왕복
- 이전 JSON의 `Unspecified` 상태 마이그레이션
- 포스터 상대 경로 저장과 해석
- 포스터 다운로드 실패 시 `Failed`
- JSON 저장 실패 시 새 포스터 정리와 이전 레코드 유지

### 수동 통합 검증

1. 저장소 루트 `.env`에 실제 TMDB Read Access Token을 설정한다.
2. `E:\MyVideo`를 라이브러리 위치로 등록한다.
3. 4개 영화, 2개 한국 드라마 에피소드, 2개 영어 시리즈 에피소드가 자동 처리되는지 확인한다.
4. 첫 검색 후보가 자동 적용되는지 확인한다.
5. 포스터가 로컬 경로로 저장되고 앱 재시작 후 표시되는지 확인한다.
6. 토큰을 제거하여 `Failed`가 저장되는지 확인한다.
7. 토큰을 복구한 뒤 다음 라이브러리 검색에서 재시도되는지 확인한다.
8. 결과가 없는 파일은 파일명 표시와 `NotFound` 상태를 유지하며 반복 검색하지 않는지 확인한다.
9. `ABOUT` 다이얼로그의 소개, 버전, TMDB 로고, 링크, 고지문과 키보드 닫기를 확인한다.

## 16. 완료 조건

- 사용하는 쪽은 구체적인 TMDB 형식 없이 `IMetadataProvider`만 호출한다.
- 새 공급자는 인터페이스 구현과 등록만으로 검색 순서에 추가할 수 있다.
- 라이브러리 검색 후 대상 영상의 메타데이터가 사용자 선택 없이 자동 적용된다.
- 검색 결과가 여러 개면 첫 후보가 적용된다.
- 검색 결과가 없으면 파일명 표시를 유지하고 `NotFound`로 저장한다.
- `NotFound`는 자동 재검색하지 않고 `Failed`는 다음 검색에서 재시도한다.
- 외부 참조는 공급자, 자원 유형, 자원 ID를 함께 저장한다.
- 포스터는 로컬 파일과 상대 경로로만 영구 저장한다.
- 기존 수동 메타데이터를 마이그레이션 과정에서 덮어쓰지 않는다.
- `E:\MyVideo`의 8개 파일명 분석 테스트가 통과한다.
- `ABOUT` 다이얼로그에서 앱 소개와 TMDB 고지를 확인할 수 있다.
- `README.md` 및 `.env.example`은 생성하거나 변경하지 않는다.
