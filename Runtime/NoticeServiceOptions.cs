using System;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary><see cref="NoticeService"/> 설정입니다.</summary>
	public sealed class NoticeServiceOptions
	{
		/// <summary>
		/// 문서를 받을 HTTP 클라이언트입니다. 필수입니다. 재시도가 필요하면 <c>RetryMiddleware</c> 를 넣습니다(GET 이라 재시도해도 됨).
		/// 공지는 로그인 전에도 받으므로 인증 토큰 미들웨어는 넣지 않아도 됩니다.
		/// </summary>
		public IHttpClient HttpClient { get; set; }

		/// <summary>문서 주소입니다. 필수입니다. 절대 주소이거나 <see cref="HttpClient"/> 기본 주소에 대한 상대 주소입니다. 빌드 환경별로 다르면 core 의 <c>EnvValue</c> 로 고릅니다.</summary>
		public string DocumentUri { get; set; }

		/// <summary>처음 언어 태그입니다(<c>ko</c>, <c>zh-Hant</c> 등). 필수입니다. 게임 언어 설정이 바뀌면 <see cref="INoticeService.Language"/> 를 바꿉니다.</summary>
		public string Language { get; set; }

		/// <summary>공지를 고를 플랫폼 이름입니다. null 이면 <see cref="NoticePlatforms.Current"/> 입니다.</summary>
		public string Platform { get; set; }

		/// <summary>문서 요청 하나의 제한 시간입니다. 넘으면 <c>ResultCodes.Timeout</c> 입니다. 기본 10초입니다.</summary>
		public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

		/// <summary>
		/// '오늘 안 보기'의 하루를 나눌 시간대입니다. null 이면 기기 시간대(<see cref="TimeZoneInfo.Local"/>)입니다.
		/// 게임의 일일 초기화 시간대에 맞추려면 그 시간대를 넣습니다.
		/// </summary>
		public TimeZoneInfo DayTimeZone { get; set; }

		/// <summary>
		/// 읽음·'오늘 안 보기' 기록의 PlayerPrefs 키입니다. 기본 <c>geuneda.notice.state</c>. 문서마다 서비스를 따로 만들면 키도 따로 둡니다
		/// (서비스는 만들 때 읽은 기록 전체를 덮어쓰므로 같은 키를 함께 쓰면 서로의 기록을 지움).
		/// </summary>
		public string StateKey { get; set; } = "geuneda.notice.state";
	}
}
