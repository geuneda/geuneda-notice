# Geuneda Notice

게임 공지 목록입니다. 모든 언어·플랫폼의 공지를 담은 JSON 문서 하나를 받아, 지금 시각·플랫폼·언어로 거르고 정렬합니다. 읽음과 '오늘 안 보기'를 기기에 기록해 안 읽은 수(레드닷)와 게임 진입 때 띄울 공지를 줍니다. 화면은 게임이 그립니다(게임 UI 팝업이든 웹뷰든). 문서 형식은 Unity 와 .NET 이 같은 소스로 읽고, ASP.NET Core 배포 헬퍼(`Server~/`)가 지금 보이는 공지만 ETag 와 함께 내려줍니다. 결과는 core 결과 규약(`Result`)을 따릅니다.

| 기능 | 타입 | 네임스페이스 |
|------|------|-------------|
| 공지 받기·거르기·기록 | `INoticeService`, `NoticeService`, `NoticeServiceOptions`, `Notice` | `Geuneda.Notice` |
| 문서 | `NoticeDocument`, `NoticeItem`, `NoticeContent`, `NoticePlatforms`, `NoticeDocumentException` | `Geuneda.Notice` |
| 코드 | `NoticeResultCodes`(10000 블록) | `Geuneda.Notice` |
| 배포(.NET, `Server~/`) | `AddGeunedaNotice`, `MapGeunedaNotice`, `NoticeServerBuilder`(`AddFileSource`, `AddSource<T>`), `INoticeSource`, `NoticeServerOptions` | `Geuneda.Notice.Server` |

## 설치

`Packages/manifest.json` 에 추가합니다. UPM 은 git 의존성을 자동으로 받지 않으므로 core, http, UniTask 도 함께 넣습니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.http": "https://github.com/geuneda/geuneda-http.git",
    "com.geuneda.notice": "https://github.com/geuneda/geuneda-notice.git"
  }
}
```

## 사용법

### 부트스트랩

```csharp
using Geuneda.Core;
using Geuneda.Http;
using Geuneda.Notice;

var notices = new NoticeService(new NoticeServiceOptions
{
    // GET 이라 재시도해도 된다. 로그인 전에도 받으므로 인증 토큰 미들웨어는 필요 없다
    HttpClient = new HttpClientBuilder().AddMiddleware(new RetryMiddleware()).Build(),
    DocumentUri = new EnvValue<string>("https://dev.example.com/notices",
                                       "https://qa.example.com/notices",
                                       "https://api.example.com/notices").Value,
    Language = settings.LanguageCode,          // 필수. "ko", "zh-Hant" 등
    // Platform 기본 NoticePlatforms.Current, RequestTimeout 기본 10초, DayTimeZone 기본 기기 시간대, StateKey 기본 geuneda.notice.state
});
MainInstaller.Bind<INoticeService>(notices);
```

### 목록과 레드닷

```csharp
var time = MainInstaller.Resolve<ITimeService>();   // services. 기기 시계를 믿지 않으면 서버 시각

var result = await notices.RefreshAsync(token);      // 로비 진입 때. 실패해도 지난 문서는 그대로
var list = notices.GetNotices(time.DateTimeUtcNow);  // 보이는 공지(정렬됨). 분류만: GetNotices(now, "event")

foreach (var notice in list)
{
    cell.Show(notice.Title, notice.Body, notice.LinkUrl, notice.LinkLabel, unread: !notices.IsRead(notice.Id));
}

notices.MarkRead(notice.Id);                          // 상세를 열 때

// 레드닷(gamesystems 의 IRedDotService 등)
notices.Changed += () => redDot.SetLeafValue(RedDotPaths.Notice, notices.GetUnreadCount(time.DateTimeUtcNow));
```

- `Changed` 는 문서가 바뀌었을 때(같은 바이트면 오지 않음), 언어를 바꿨을 때, 읽음·'오늘 안 보기'를 기록했을 때 옵니다. **공지 기간의 시작·끝에는 오지 않으므로** 공지 화면과 레드닷은 화면에 들어올 때도 다시 셉니다.
- 게임 언어 설정이 바뀌면 `notices.Language = "en"` 로 바꿉니다.
- 메인 스레드에서 씁니다. `Changed` 도 메인 스레드에서 옵니다.

### 게임 진입 때 띄울 공지

```csharp
var now = time.DateTimeUtcNow;

foreach (var notice in notices.GetPopups(now))   // popup 공지 중 '오늘 안 보기'가 아닌 것(정렬됨). 읽음은 보지 않음
{
    await ShowNoticePopupAsync(notice, onTodayClose: () => notices.SnoozeForToday(notice.Id, time.DateTimeUtcNow));
}
```

- '오늘 안 보기'는 하루 기준 시간대(`DayTimeZone`, 기본 기기 시간대)의 **다음 날이 시작하는 순간**까지입니다. 자정이 두 번 있는 날(일광 절약 시간 끝)은 지금 이후 먼저 오는 자정, 자정이 없는 날(일광 절약 시간 시작)은 시계가 넘어간 순간입니다. 게임의 일일 초기화 시간대에 맞추려면 그 시간대를 넣습니다.
- 차례로 띄우는 일은 게임 UI(uiservice 의 팝업 큐 등)가 합니다.
- 한 번 읽은 팝업을 다시 띄우지 않으려면 `GetPopups(now).Where(n => !notices.IsRead(n.Id))` 처럼 거릅니다.
- `notice.Action`(예: `shop:gem_100`)은 게임이 해석하는 동작입니다(누르면 상점 열기 등).

### 보이는 공지

| 조건 | 규칙 |
|------|------|
| 기간 | `startsAt <= now < endsAt`(없으면 그쪽 끝없음). `now` 는 넘긴 UTC 시각(UTC 가 아니면 예외) |
| 플랫폼 | `platforms` 가 없으면 모두, 있으면 `Platform`(기본 `NoticePlatforms.Current`: 에디터는 활성 빌드 대상) 이 목록에 있어야 함 |
| 언어 | `Language`(예: `zh-Hant-TW`) → 뒤 부분을 뺀 태그(`zh-Hant`, `zh`) → 문서 `defaultLanguage` 순서로 찾고 없으면 보이지 않음. 대소문자와 `_`/`-` 는 가리지 않음 |
| 분류 | `GetNotices`·`GetUnreadCount` 에 분류를 넘기면 그 분류만(대소문자 가림) |
| 정렬 | `priority` 큰 것 → `startsAt` 늦은 것(없으면 가장 이른 것) → 문서 순서 |

### 기록

- 읽음·'오늘 안 보기'는 PlayerPrefs 키 하나(`StateKey`)에 JSON 으로 둡니다(계정이 아니라 기기 단위). 쓸 때마다 `PlayerPrefs.Save` 합니다.
- **받은 문서에 없다는 이유로는 기록을 지우지 않습니다.** 문서는 그때 보이는 공지 목록일 뿐이라(서버가 끝난 공지를 뺌), 오래된 CDN 사본이나 잠시 내린 공지 한 번에 읽음·'오늘 안 보기'를 잃지 않게 합니다.
- 대신 읽음은 **최근 500개**만 남기고, 넘으면 가장 먼저 읽은 것부터 지웁니다(기록 크기 상한). 지난 '오늘 안 보기'는 다음 '오늘 안 보기' 기록 때 함께 지웁니다.
- 여러 문서를 각각의 서비스로 받으면 `StateKey` 를 따로 둡니다(서비스는 기록 전체를 한 번에 쓰므로 같은 키를 함께 쓰면 서로의 기록을 덮어씀).
- 기록을 읽지 못하면(손상) 경고 뒤 빈 기록으로 시작하고, 저장하지 못하면(WebGL 저장 한도) 경고 뒤 이번 실행은 메모리 기록으로 계속합니다.
- `MarkRead` 는 문서에 없는 ID 도 기록합니다(웹 페이지가 알려 준 ID 등).

### 결과

| 상황 | 결과 코드 |
|------|-----------|
| 성공(새 문서, 304, 같은 바이트) | 0 |
| 연결 실패, 제한 시간, 이 호출의 취소 | `ResultCodes.NetworkError`, `Timeout`, `Cancelled` |
| 2xx·304 가 아닌 응답, 조건부 요청이 아닌데 304 | `HttpResultCodes.HttpError` |
| 문서가 형식이나 규칙에 맞지 않음(지금 문서 유지, 경고 로그) | `NoticeResultCodes.InvalidDocument`(10001) |
| 필수 설정 없음, 잘못된 언어 태그·플랫폼 이름·공지 ID, UTC 가 아닌 시각 | 예외 |

### 갱신 규칙

- **디스크에 두지 않습니다.** 문서는 이 실행의 메모리에만 있습니다. 오프라인으로 시작하면 공지가 없습니다(레퍼런스 Percent 와 같음).
- **조건부 요청**: 받은 적이 있고 서버가 ETag 를 줬으면 `If-None-Match` 로 묻고, 304 면 그대로 둡니다. ETag 가 없는 서버(정적 호스팅)여도 같은 바이트면 `Changed` 가 오지 않습니다.
- **single-flight**: 동시에 부르면 요청 하나를 함께 기다립니다. 호출자 토큰은 그 호출의 기다림만 멈추고 요청은 제한 시간까지 계속되어 결과가 적용됩니다.

## 공지 문서

```json
{
  "defaultLanguage": "en",
  "notices": [
    {
      "id": "update-2-1",
      "category": "notice",
      "priority": 10,
      "popup": true,
      "action": "shop:gem_100",
      "startsAt": "2026-10-17T00:00:00Z",
      "endsAt": "2026-10-31T00:00:00Z",
      "platforms": ["android", "ios"],
      "contents": {
        "ko": { "title": "2.1 업데이트", "body": "본문", "linkUrl": "https://example.com/notice/2-1", "linkLabel": "자세히" },
        "en": { "title": "Update 2.1" }
      }
    }
  ]
}
```

| 값 | 규칙 |
|----|------|
| `notices` | 필수 배열(빈 배열 가능). 문서 순서가 정렬의 마지막 기준 |
| `defaultLanguage` | 선택. 공지에 기기 언어 내용이 없을 때 쓸 언어 태그 |
| `id` | 필수. 1~128자 영문·숫자·`.`·`_`·`-`, 대소문자 가림, 문서 안에서 유일. **읽음 기록의 키라 내용을 고쳐도 바꾸지 않습니다**(바꾸면 모두에게 다시 안 읽음) |
| `category` | 선택(기본 `notice`). 1~64자 영문·숫자·`.`·`_`·`-`. 공지·이벤트 탭 등 게임이 정함 |
| `priority` | 선택 정수(기본 0). 클수록 앞 |
| `startsAt`·`endsAt` | 선택. 시간대(`Z` 나 오프셋)가 있는 ISO 8601. 시작 포함·끝 제외, 끝은 시작보다 뒤 |
| `platforms` | 선택. 없으면 모든 플랫폼. 1~32자 영문 소문자·숫자·`_`·`-`(`android`, `ios`, `standalone`, `webgl` 또는 게임이 정한 이름). 빈 배열·겹침은 거절 |
| `popup` | 선택(기본 false). 게임 진입 때 띄울 공지 |
| `action` | 선택. 게임이 해석하는 동작 문자열(1~256자) |
| `contents` | 필수. 언어 태그(영문·숫자 부분을 `-`·`_` 로 이음, 1~35자, 대소문자·`_`/`-` 를 무시하고 겹치면 거절) → 내용. 하나 이상 |
| `title` | 필수, 비어 있으면 안 됨 |
| `body`, `linkLabel` | 선택 문자열. 본문 표시 형식(일반 글, TMP 리치 텍스트 등)은 게임이 정함 |
| `linkUrl` | 선택. `http`·`https` 절대 주소만(`javascript:`, `file:`, 상대 주소, 게임 스킴은 거절. 게임 안 이동은 `action` 으로) |

- 같은 이름의 속성 두 번, JSON 뒤의 다른 내용, UTF-8 이 아닌 바이트는 거절합니다. JSON `null` 은 없는 값으로 봅니다.
- **모르는 속성**: 클라이언트는 무시합니다(다음 판의 서버가 속성을 더해도 옛 클라이언트가 읽음). 서버의 파일 원천과 `NoticeDocument.Parse(bytes, strict: true)` 는 거절합니다(`startAt` 같은 오타로 기간이 빠진 공지가 끝없이 보이지 않게). 운영 도구도 엄격하게 읽어 검사하세요.
- 문서 해석·쓰기 코드는 클라이언트(`Runtime/Document/`)와 서버가 같은 파일을 컴파일하고, 같은 문서 테스트를 Unity 와 .NET 이 함께 돌립니다.

## 배포 서버 (`Server~/`)

.NET 10 솔루션 `Server~/Geuneda.Notice.Server.slnx` 입니다. 게임 서버는 `src/Geuneda.Notice.Server` 를 `ProjectReference` 로 참조합니다.

```csharp
using Geuneda.Notice.Server;

// 파일 원천: 콘텐츠 루트 기준 상대 경로. 1초에 한 번까지 다시 읽고, 잘못된 파일이면 오류 로그 뒤 이전 문서를 계속 내려줌
builder.Services.AddGeunedaNotice().AddFileSource("notices.json");
// 또는 게임 원천(DB·운영 도구): builder.Services.AddGeunedaNotice().AddSource<DbNoticeSource>(ServiceLifetime.Scoped);

var app = builder.Build();
app.MapGeunedaNotice("/notices");   // GET. 요청 수 제한 등은 돌려준 빌더에 붙임
```

- **지금 보이는 공지만 내려줍니다.** 서버 시각(`TimeProvider`)으로 아직 시작하지 않은 공지(미리 알리지 않을 내용)와 끝난 공지를 뺍니다. 클라이언트도 넘긴 시각으로 다시 거릅니다.
- 응답에는 ETag(내려준 바이트의 SHA-256)와 `Cache-Control`(기본 `no-cache`, `NoticeServerOptions.CacheControl`)이 붙고, `If-None-Match` 가 맞으면 304 입니다(약한 비교, `*` 포함). 기간 경계에서 내용이 바뀌므로 긴 `max-age` 는 경계를 늦춥니다.
- **원천(`INoticeSource`)**: 요청마다 불립니다. 문서가 그대로면 같은 `NoticeDocument` 인스턴스를 돌려주세요. 같은 인스턴스이고 기간 경계를 지나지 않았으면 직렬화·ETag 를 다시 계산하지 않습니다. 코드로 만든 문서(`new NoticeItem(...)`)도 같은 규칙으로 검사합니다(어기면 `NoticeDocumentException`). null 을 돌려주면 500 입니다.
- **시작 검사**: `MapGeunedaNotice` 는 `AddGeunedaNotice`·원천이 없으면 예외이고, 파일 원천이면 여기서 파일을 엄격하게 읽어 잘못되면 시작을 막습니다. 게임 원천은 DB 가 준비되기 전일 수 있어 읽지 않습니다.
- 파일을 운영 중에 바꿀 때는 임시 파일에 쓴 뒤 이름을 바꿉니다(반쯤 쓴 파일을 읽지 않게).

샘플 서버와 Unity 상호 운용 테스트:

```bash
dotnet run --project "Server~/samples/Geuneda.Notice.SampleServer" --no-launch-profile -- --urls http://127.0.0.1:5599
# Unity: GEUNEDA_NOTICE_INTEROP_URL=http://127.0.0.1:5599/ 로 SampleServerInteropTest 를 이름으로 골라 실행
```

## 레퍼런스와 다른 점

레퍼런스는 Lucky Defense 의 Percent `WebView.Notice` 와 사용자 게임(SpellDefense)의 공지 서비스입니다.

- **따른 것**: 공지 ID 별 로컬 읽음 기록으로 '새 공지'를 판정(서버 최신 시각 비교 없음), 기간·OS·언어 필터, 기본 언어 대체, 레드닷 갱신 콜백, 진입 팝업과 '오늘 안 보기'(SpellDefense).
- **보완한 것**:
  - 읽음 기록이 끝없이 쌓이지 않음: 레퍼런스는 공지마다 PlayerPrefs 키를 만들고 지우지 않습니다. 여기서는 키 하나에 최근 500개를 둡니다.
  - 서버가 기간 밖 공지를 뺌: 레퍼런스는 클라이언트가 기간을 거르므로 목록 JSON 에 기간 밖 공지가 들어 있을 수 있습니다(추정).
  - 조건부 요청(ETag/304)과 single-flight, 요청 제한 시간: 레퍼런스 목록 요청에서는 조건부 요청을 찾지 못했습니다.
  - '오늘'의 기준이 기기 시간대의 자정: SpellDefense 는 UTC 날짜 문자열이라 한국에서는 오전 9시에 풀립니다.
  - 언어 태그를 줄여 가며 찾음(`zh-Hant-TW` → `zh-Hant` → `zh`): 레퍼런스는 정확한 언어 키와 기본 키만 봅니다.
  - 서버 운영 파일의 오타를 시작·교체 때 거절(엄격한 읽기).
- 웹 페이지 공지는 `com.geuneda.webview` 의 공지 명령(`notice.list`·`notice.read`·`notice.refresh`, 이벤트 `notice.changed`)이 이 서비스의 목록과 기록을 그대로 씁니다.
