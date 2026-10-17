using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>
	/// 문서의 이름 규칙(공지 ID, 분류, 언어 태그, 플랫폼 이름, 링크)입니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>
	/// 런타임의 문자 표에 기대지 않도록 ASCII 만 받고 ASCII 로만 소문자로 바꿉니다(<c>ToLowerInvariant</c> 는 Mono 와 .NET 의 표가 다름).
	/// </remarks>
	internal static class NoticeNames
	{
		public const int MaxIdLength = 128;
		public const int MaxCategoryLength = 64;
		public const int MaxLanguageLength = 35;
		public const int MaxPlatformLength = 32;
		public const int MaxActionLength = 256;

		/// <summary>공지 ID: 1~128자, 영문·숫자·<c>.</c>·<c>_</c>·<c>-</c>. 대소문자를 가립니다.</summary>
		public static bool IsId(string value)
		{
			return IsToken(value, MaxIdLength);
		}

		/// <summary>분류: 1~64자, 영문·숫자·<c>.</c>·<c>_</c>·<c>-</c>. 대소문자를 가립니다.</summary>
		public static bool IsCategory(string value)
		{
			return IsToken(value, MaxCategoryLength);
		}

		/// <summary>플랫폼 이름: 1~32자, 영문 소문자·숫자·<c>_</c>·<c>-</c>.</summary>
		public static bool IsPlatform(string value)
		{
			if (string.IsNullOrEmpty(value) || value.Length > MaxPlatformLength)
			{
				return false;
			}

			foreach (var c in value)
			{
				if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// 언어 태그를 비교용으로 바꿉니다: 영문·숫자로 된 부분을 <c>-</c>(또는 <c>_</c>)로 이은 1~35자, 소문자, <c>_</c> 는 <c>-</c> 로(<c>zh_TW</c> → <c>zh-tw</c>).
		/// 규칙에 맞지 않으면 null 입니다.
		/// </summary>
		public static string NormalizeLanguage(string value)
		{
			if (string.IsNullOrEmpty(value) || value.Length > MaxLanguageLength)
			{
				return null;
			}

			var chars = new char[value.Length];
			var previousSeparator = true;

			for (var i = 0; i < value.Length; i++)
			{
				var c = value[i];

				if (c == '-' || c == '_')
				{
					if (previousSeparator)
					{
						return null;
					}

					chars[i] = '-';
					previousSeparator = true;
					continue;
				}

				if (c >= 'A' && c <= 'Z')
				{
					c = (char) (c + ('a' - 'A'));
				}
				else if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
				{
					return null;
				}

				chars[i] = c;
				previousSeparator = false;
			}

			return previousSeparator ? null : new string(chars);
		}

		/// <summary>링크: <c>http</c> 나 <c>https</c> 절대 주소입니다(<c>javascript:</c>, <c>file:</c> 등은 받지 않음).</summary>
		public static bool IsHttpUrl(string value)
		{
			return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
		}

		private static bool IsToken(string value, int maxLength)
		{
			if (string.IsNullOrEmpty(value) || value.Length > maxLength)
			{
				return false;
			}

			foreach (var c in value)
			{
				if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.'))
				{
					return false;
				}
			}

			return true;
		}
	}
}
