# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.1.0] - 2026-10-17

**New**:
- `INoticeService`/`NoticeService`: 공지 문서를 ETag 조건부 요청(304)·single-flight·요청 제한 시간으로 받아 메모리에 둠. 실패하면 지금 문서 유지(`NoticeResultCodes.InvalidDocument` 10001, http·core 공용 코드)
- 보이는 공지(`GetNotices`): 넘긴 UTC 시각 기준 기간(시작 포함·끝 제외), 플랫폼(`NoticePlatforms.Current` 기본), 언어(태그를 줄여 가며 찾고 문서 기본 언어로), 분류로 거르고 우선순위 → 늦은 시작 → 문서 순서로 정렬
- 읽음(`MarkRead`, `IsRead`, `GetUnreadCount`)과 '오늘 안 보기'(`SnoozeForToday`, `IsSnoozed`, 하루 기준 시간대의 다음 날 시작까지), 진입 팝업(`GetPopups`). 기록은 PlayerPrefs 키 하나에 두고 받은 문서에 없다는 이유로는 지우지 않음(읽음은 최근 500개). '오늘'의 끝은 일광 절약 시간의 두 번 있는·없는 자정도 맞게 계산
- `Changed`: 문서·언어·기록이 바뀌면 옴(처리기 예외는 로그)
- 문서 형식(`NoticeDocument`, `NoticeItem`, `NoticeContent`): 모든 언어·플랫폼의 공지를 담은 JSON. 클라이언트는 모르는 속성을 무시하고 엄격한 읽기(`strict`)는 거절. 결정적인 쓰기(`ToUtf8Json`)와 기간 거르기(`ActiveAt`). Unity 와 .NET 이 같은 소스로 읽고 같은 문서 테스트를 돌림
- `Server~`: ASP.NET Core 배포 헬퍼 `Geuneda.Notice.Server`(`AddGeunedaNotice`, `AddFileSource`, `AddSource<T>`, `MapGeunedaNotice`). 지금 보이는 공지만 ETag(SHA-256)·Cache-Control 과 함께 내려주고 `If-None-Match` 면 304. 파일 원천은 시작 때 엄격하게 읽고 1초마다 내용을 비교해 바꾸며 잘못된 파일은 거절. 샘플 서버와 Unity 상호 운용 테스트
