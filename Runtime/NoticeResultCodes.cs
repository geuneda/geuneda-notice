// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>
	/// 공지 패키지의 결과 코드입니다(core 결과 코드 범위 표의 10000 블록). 연결 실패·제한 시간·취소는 core 공용 코드, 2xx·304 가 아닌 응답은 <c>HttpResultCodes.HttpError</c> 입니다.
	/// </summary>
	public static class NoticeResultCodes
	{
		/// <summary>받은 공지 문서가 형식이나 규칙에 맞지 않습니다. 지금 문서를 그대로 둡니다</summary>
		public const int InvalidDocument = 10001;
	}
}
