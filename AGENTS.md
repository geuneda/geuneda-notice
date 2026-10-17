# Geuneda.Notice - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.notice`
- **Unity**: 2021.3+
- **의존성**: `com.geuneda.core`(ModuleLogger, Result), `com.geuneda.http`(문서 GET), `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`. 배포 헬퍼(`Server~`)는 .NET 10, ASP.NET Core, Newtonsoft.Json 13.0.3 입니다
- **역할**: 공지 문서를 받아 지금 시각·플랫폼·언어로 거르고, 읽음·'오늘 안 보기'를 기기에 기록합니다. 화면은 게임 몫입니다(게임 UI 팝업, 웹뷰)
- **서버 공유**: `Server~` 의 라이브러리가 `Runtime/Document/*.cs` 를 그대로 링크합니다. 서버가 쓴 문서를 클라이언트가 같은 코드로 읽는 것이 이 패키지의 핵심 계약입니다
- **레퍼런스**: Percent `WebView.Notice`(공지 ID 별 로컬 읽음, 기간·OS·언어 필터)와 SpellDefense `NoticeService`(진입 팝업, 오늘 안 보기). 분석은 lucky-defense 레포 `data/analysis/tech/percent/analytics-liveops.md` 3절

## 2. 구조

| 파일 | 역할 |
|------|------|
| `Runtime/INoticeService.cs`, `Runtime/NoticeService.cs`, `Runtime/NoticeServiceOptions.cs` | 공개 계약과 구현. 받기(single-flight, ETag), 거르기·정렬, 기록, `Changed` |
| `Runtime/Notice.cs` | 보여 줄 공지(문서 항목 + 지금 언어 내용) |
| `Runtime/NoticeResultCodes.cs`, `Runtime/NoticePlatforms.Current.cs` | 결과 코드(10000 블록), 빌드 대상 플랫폼 이름 |
| `Runtime/Internal/NoticeState.cs`, `NoticeStateStore.cs` | 기록 형식(JSON)과 저장소(internal `INoticeStateStore`, 기본 `PlayerPrefsNoticeStateStore`) |
| `Runtime/Document/` | 문서 형식(`NoticeDocument`, `NoticeItem`, `NoticeContent`, `NoticePlatforms`, `NoticeDocumentException`, internal `NoticeJson`·`NoticeNames`). **서버와 함께 컴파일** |
| `Runtime/AssemblyInfo.cs` | 테스트 어셈블리(`Geuneda.Notice.Tests.EditMode`)에 internal 공개 |
| `Server~/` | .NET 솔루션 `Geuneda.Notice.Server.slnx`: `src/Geuneda.Notice.Server`(배포 헬퍼, `Runtime/Document/*.cs` 링크), `tests/Geuneda.Notice.Document.Tests`(NUnit 3, `Tests/EditMode/Document/*.cs` 링크), `tests/Geuneda.Notice.Server.Tests`(xUnit, 실제 Kestrel), `samples/Geuneda.Notice.SampleServer`(파일 원천) |

## 3. 규칙

- **`Runtime/Document/*.cs` 와 `Tests/EditMode/Document/*.cs` 는 Unity 와 .NET 이 함께 컴파일합니다.** C# 9 까지만 쓰고(`Server~/Directory.Build.props` 가 `LangVersion 9.0`), UnityEngine·UniTask·core 를 넣지 않습니다. 문서 테스트는 공개 API 와 NUnit 3 의 `Assert.That` 제약식만 씁니다. 새 파일을 넣으면 `dotnet test` 와 Unity 테스트로 두 쪽을 확인합니다.
- **런타임의 문자 표에 기대지 않습니다.** 이름 규칙(공지 ID, 분류, 언어 태그, 플랫폼)은 ASCII 만 받고 소문자 바꾸기도 ASCII 로만 합니다(banword 에서 `ToUpperInvariant` 가 Mono 와 .NET 에서 224자 다름을 확인). 비교는 `StringComparer.Ordinal` 입니다. 시각은 `InvariantCulture` 와 정해진 형식으로만 읽고 씁니다.
- **문서는 만든 뒤 바뀌지 않습니다.** 생성자가 입력을 복사하고 규칙을 검사합니다(어기면 `NoticeDocumentException`). 서버가 여러 요청에서 함께 쓰고 배포기가 인스턴스로 캐시를 판단합니다.
- **두 가지 읽기**: 클라이언트는 모르는 속성을 무시하고(옛 클라이언트가 새 서버 문서를 읽게), 서버 파일 원천은 `strict` 로 거절합니다(오타로 기간이 빠지지 않게). 값 오류는 두 쪽 모두 문서 전체 거절입니다(클라이언트는 지금 문서 유지).
- **쓰기는 결정적입니다**(`ToUtf8Json`): 언어는 이름 순, 기본값 속성은 뺌, 시각은 `yyyy-MM-ddTHH:mm:ss.FFFFFFFZ`. 서버 ETag 가 이 바이트의 해시라, 쓰기를 바꾸면 모든 클라이언트가 한 번 새로 받습니다.
- **시각은 호출자가 넘깁니다**(appsetting `GetAccess(utcNow)` 와 같음). 서비스는 시계를 갖지 않고, UTC 가 아니면 예외입니다. 기록 정리도 시계 없이 합니다: 지난 '오늘 안 보기'는 다음 '오늘 안 보기' 기록 때 지우고, 읽음은 최근 `NoticeState.MaxReadRecords`(500)개만 남깁니다.
- **받은 문서에 없다는 이유로 기록을 지우지 않습니다.** 문서는 그때 보이는 목록이고 최신성 보장이 없어, 오래된 CDN 사본 한 번에 읽음·'오늘 안 보기'를 영구히 잃습니다(0.1.0 리뷰 1회차 🔴, 처음 구현이 그랬음). 정리 정책을 바꾸려면 `Refresh_DocumentWithoutAnId_KeepsItsRecords` 가 지키는 이 경우를 먼저 봅니다.
- **'오늘'의 끝**(`NextDayStart`): `DayTimeZone`(기본 `TimeZoneInfo.Local`)의 다음 날이 시작하는 UTC 순간. 자정이 두 번 있으면(`IsAmbiguousTime`) 오프셋별 UTC 후보 중 지금 이후 가장 이른 것(날짜를 넘어 전날로 돌아간 뒤에는 이른 후보가 이미 지남, 리뷰 2회차 🟡), 없으면(`IsInvalidTime`) 지역 시각이 자정 이상이 되는 첫 UTC 순간을 이분 탐색으로 찾습니다(전환 폭이 30분 단위가 아닌 시간대도 맞음, 리뷰 1회차 🟡).
- **실패 정책**: 받기 실패·문서 오류는 결과 코드이고 지금 문서를 유지합니다(경고 로그는 문서 오류만). 기록 읽기 실패는 경고 뒤 빈 기록, 쓰기 실패(`PlayerPrefsException`)는 경고 뒤 메모리 기록으로 계속합니다. 그 밖의 예외는 버그라 잡지 않습니다. `Changed` 처리기 예외만 처리기마다 잡아 Error 로그입니다.
- **single-flight**: appsetting 과 같은 구조입니다. 진행 중인 요청 하나를 `UniTaskCompletionSource` 로 나눠 받고, 호출자 토큰은 기다림만 멈춥니다(요청에는 토큰을 넘기지 않고 요청 제한 시간이 끝을 보장).
- **서버는 지금 보이는 공지만 내려줍니다.** 배포기(`NoticePublisher`)는 같은 문서 인스턴스이고 지금 시각이 직전 결과의 기간 경계 구간([가장 늦은 지난 경계, 가장 이른 다가올 경계)) 안이면 직렬화·해시를 다시 하지 않습니다. 시계가 뒤로 가도 구간 밖이면 다시 계산합니다.
- **파일 원천만 시작 때 읽습니다.** `AddFileSource` 는 `NoticeFileSource` 를 따로 등록하고 `AddSource<T>` 는 그 등록을 지워, `MapGeunedaNotice` 는 `NoticeFileSource` 가 등록되어 있을 때만 읽습니다(게임 원천을 루트 범위에서 만들지 않음).
- **static 가변 상태 금지**(D2). 로거만 static 입니다.

## 4. 테스트

- Unity: EditMode `Geuneda.Notice.Tests.EditMode`(core·http·notice·UniTask·Newtonsoft 가 있는 프로젝트). `FakeNoticeServer`(가짜 `IHttpTransport`, 게이트로 동시 요청), `MemoryStateStore`(재시작 흉내), 실제 PlayerPrefs 저장소 테스트, `SampleServerInteropTest`(명시 실행, `GEUNEDA_NOTICE_INTEROP_URL`).
- .NET: `dotnet test "Server~/Geuneda.Notice.Server.slnx"`. 문서 테스트(Unity 와 같은 파일)와 서버 테스트(127.0.0.1 빈 포트의 실제 Kestrel, `FakeTimeProvider` 로 기간 경계·파일 다시 읽기).
- 일광 절약 시간 테스트는 `TimeZoneInfo.CreateCustomTimeZone` 의 사용자 정의 규칙으로 합니다(기기 시간대에 기대지 않음).

## 5. 문서

- 공개 API, 문서 형식, 거르기·기록 규칙을 바꾸면 `README.md`, `CHANGELOG.md`, `package.json` 버전, geuneda-frameworks 호스트 레포의 `claude-skills/geuneda-notice/`(SKILL.md, references/api.md)와 `claude-skills/geuneda-frameworks/` 를 함께 고치고 `claude-skills/install.sh geuneda-notice`, `claude-skills/install.sh geuneda-frameworks` 로 설치합니다.
- `Runtime/Document` 를 바꾸면 서버와 클라이언트가 함께 바뀌므로 `dotnet test` 와 Unity 테스트, 상호 운용 테스트를 함께 돌립니다.
