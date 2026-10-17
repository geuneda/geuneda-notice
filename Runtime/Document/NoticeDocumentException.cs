using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>
	/// 공지 문서를 읽거나 만들지 못했습니다(형식 오류, 규칙 위반). 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>
	/// 클라이언트(<c>NoticeService</c>)는 이 예외를 <c>NoticeResultCodes.InvalidDocument</c> 실패로 바꿔 돌려주고, 서버는 그대로 던집니다(시작 실패, 파일 원천의 교체 거절).
	/// </remarks>
	public sealed class NoticeDocumentException : Exception
	{
		public NoticeDocumentException(string message) : base(message)
		{
		}
	}
}
