// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>공지 하나의 한 언어 내용입니다. 만든 뒤 바뀌지 않습니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.</summary>
	public sealed class NoticeContent
	{
		/// <param name="title">제목. 비어 있으면 안 됩니다</param>
		/// <param name="body">본문. 없으면 null. 표시 형식(일반 글, TMP 리치 텍스트 등)은 게임이 정합니다</param>
		/// <param name="linkUrl">링크. 없으면 null. <c>http</c>·<c>https</c> 절대 주소만 받습니다</param>
		/// <param name="linkLabel">링크 버튼 문구. 없으면 null</param>
		/// <exception cref="NoticeDocumentException">제목이 비었거나 링크가 http·https 절대 주소가 아닌 경우</exception>
		public NoticeContent(string title, string body = null, string linkUrl = null, string linkLabel = null)
		{
			if (string.IsNullOrEmpty(title))
			{
				throw NoticeJson.Invalid("Notice title is required");
			}

			if (linkUrl != null && !NoticeNames.IsHttpUrl(linkUrl))
			{
				throw NoticeJson.Invalid($"Notice link must be an absolute http or https URL: '{linkUrl}'");
			}

			Title = title;
			Body = body;
			LinkUrl = linkUrl;
			LinkLabel = linkLabel;
		}

		public string Title { get; }

		public string Body { get; }

		public string LinkUrl { get; }

		public string LinkLabel { get; }
	}
}
