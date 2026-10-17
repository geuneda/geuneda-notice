namespace Geuneda.Notice.Server
{
	/// <summary>공지 배포 설정입니다.</summary>
	public sealed class NoticeServerOptions
	{
		/// <summary>
		/// 응답의 Cache-Control 입니다. 기본 <c>no-cache</c>(CDN·클라이언트가 둬도 되지만 매번 ETag 로 다시 확인). 기간 경계에 맞춰 바뀌는 문서라 긴 max-age 는 경계를 늦춥니다.
		/// </summary>
		public string CacheControl { get; set; } = "no-cache";
	}
}
