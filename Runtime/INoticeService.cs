using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>
	/// 공지 목록입니다. 기본 구현은 <see cref="NoticeService"/> 이고, services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 문서는 이 실행의 메모리에만 둡니다(디스크에 두지 않음). 읽음과 '오늘 안 보기' 기록만 기기에 남깁니다(PlayerPrefs, 계정이 아니라 기기 단위).
	/// </para>
	/// <para>
	/// 보이는 공지는 기간 안이고(넘긴 <c>utcNow</c> 기준, 시작 포함·끝 제외), 이 플랫폼 대상이고, 지금 언어(없으면 문서 기본 언어)의 내용이 있는 공지입니다.
	/// 우선순위가 큰 것, 같으면 시작 시각이 늦은 것(시작 시각 없음은 가장 이른 것), 같으면 문서 순서로 정렬합니다.
	/// </para>
	/// <para>시각을 받는 메서드는 UTC 가 아니면 <see cref="ArgumentException"/> 입니다. 기기 시계를 믿지 않으면 서버 시각(services <c>ITimeService.DateTimeUtcNow</c>)을 넘깁니다.</para>
	/// <para>메인 스레드에서 씁니다. <see cref="Changed"/> 도 메인 스레드에서 옵니다.</para>
	/// </remarks>
	public interface INoticeService
	{
		/// <summary>이 실행에서 마지막으로 받아들인 문서입니다. 처음 갱신이 성공하기 전에는 null 입니다.</summary>
		NoticeDocument Document { get; }

		/// <summary>
		/// 내용을 고를 언어 태그(<c>ko</c>, <c>zh-Hant</c> 등)입니다. 바꾸면 <see cref="Changed"/> 가 옵니다(같은 태그면 오지 않음, 대소문자·<c>_</c>/<c>-</c> 무시).
		/// </summary>
		/// <exception cref="ArgumentException">태그가 규칙에 맞지 않는 경우(설정할 때)</exception>
		string Language { get; set; }

		/// <summary>
		/// 보이는 공지·안 읽은 수·띄울 공지가 바뀔 수 있으면 옵니다: 문서가 바뀌었을 때(같은 바이트면 오지 않음), 언어를 바꿨을 때, 읽음·'오늘 안 보기'를 기록했을 때.
		/// 기간 경계(공지 시작·끝)에는 오지 않으므로 레드닷은 화면에 들어올 때도 다시 셉니다. 처리기 예외는 로그만 남깁니다.
		/// </summary>
		event Action Changed;

		/// <summary>
		/// 문서를 받아 <see cref="Document"/> 를 바꿉니다. 받은 적이 있고 서버가 ETag 를 줬으면 <c>If-None-Match</c> 로 묻고, 304 면 그대로 둡니다.
		/// </summary>
		/// <remarks>
		/// <para>
		/// 동시에 부르면 요청 하나를 함께 기다립니다. <paramref name="cancellationToken"/> 은 이 호출의 기다림만 멈추고(<c>ResultCodes.Cancelled</c>), 요청은 제한 시간까지 계속되어 결과가 적용됩니다.
		/// </para>
		/// <para>
		/// 문서에 없는 공지의 기록은 지우지 않습니다(오래된 CDN 사본이나 잠시 내린 공지 한 번에 읽음·'오늘 안 보기'를 잃지 않게).
		/// </para>
		/// <para>
		/// 실패: 연결 실패·제한 시간·취소는 core 공용 코드, 2xx·304 가 아닌 응답은 <c>HttpResultCodes.HttpError</c>, 문서 오류는 <see cref="NoticeResultCodes.InvalidDocument"/> 이고
		/// 지금 문서를 그대로 둡니다.
		/// </para>
		/// </remarks>
		UniTask<Result> RefreshAsync(CancellationToken cancellationToken = default);

		/// <summary>보이는 공지입니다(정렬됨). 문서가 없으면 빈 목록입니다.</summary>
		/// <param name="category">이 분류만. null 이면 모두</param>
		IReadOnlyList<Notice> GetNotices(DateTime utcNow, string category = null);

		/// <summary>보이는 공지 중 읽지 않은 수입니다(레드닷).</summary>
		/// <param name="category">이 분류만. null 이면 모두</param>
		int GetUnreadCount(DateTime utcNow, string category = null);

		/// <summary>
		/// 게임 진입 때 띄울 공지입니다: 보이는 공지 중 <see cref="NoticeItem.Popup"/> 이고 '오늘 안 보기' 중이 아닌 것(정렬됨). 읽음은 보지 않습니다
		/// (읽은 공지를 빼려면 <see cref="IsRead"/> 로 거릅니다).
		/// </summary>
		IReadOnlyList<Notice> GetPopups(DateTime utcNow);

		bool IsRead(string id);

		/// <summary>
		/// 읽음으로 기록합니다. 처음 기록하면 <see cref="Changed"/> 가 옵니다. 문서에 없는 ID 도 기록합니다(웹 페이지가 알려 준 ID 등).
		/// 최근 500개만 남기고 넘으면 가장 먼저 읽은 것부터 지웁니다.
		/// </summary>
		/// <exception cref="ArgumentException">ID 가 공지 ID 규칙에 맞지 않는 경우</exception>
		void MarkRead(string id);

		/// <summary>'오늘 안 보기' 중인지입니다.</summary>
		bool IsSnoozed(string id, DateTime utcNow);

		/// <summary>
		/// '오늘 안 보기'를 기록합니다. 하루 기준 시간대(<see cref="NoticeServiceOptions.DayTimeZone"/>)의 다음 날이 시작할 때까지 <see cref="GetPopups"/> 에서 뺍니다(자정이 두 번 있으면 지금 이후 먼저 오는 자정, 없으면 시계가 넘어간 순간).
		/// </summary>
		/// <exception cref="ArgumentException">ID 가 공지 ID 규칙에 맞지 않거나 시각이 UTC 가 아닌 경우</exception>
		void SnoozeForToday(string id, DateTime utcNow);
	}
}
