using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>
	/// 문서의 공지 하나입니다(모든 언어 내용 포함). 만든 뒤 바뀌지 않습니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	public sealed class NoticeItem
	{
		/// <summary>분류를 주지 않았을 때의 분류입니다.</summary>
		public const string DefaultCategory = "notice";

		private static readonly IReadOnlyList<string> AllPlatforms = Array.Empty<string>();

		/// <param name="id">공지 ID. 1~128자 영문·숫자·<c>.</c>·<c>_</c>·<c>-</c>, 대소문자를 가림. 읽음 기록의 키라 내용을 고쳐도 바꾸지 않습니다</param>
		/// <param name="contents">언어 태그(<c>ko</c>, <c>zh-Hant</c> 등, 대소문자·<c>_</c>/<c>-</c> 무시) → 내용. 하나 이상</param>
		/// <param name="category">분류(공지·이벤트 등, 게임이 정함). null 이면 <see cref="DefaultCategory"/></param>
		/// <param name="priority">정렬 우선순위. 클수록 앞</param>
		/// <param name="startsAt">보이기 시작하는 시각(UTC, 포함). null 이면 처음부터</param>
		/// <param name="endsAt">보이지 않게 되는 시각(UTC, 제외). null 이면 끝없음</param>
		/// <param name="platforms">보일 플랫폼 이름(<see cref="NoticePlatforms"/> 등). null 이면 모든 플랫폼. 빈 목록은 받지 않습니다</param>
		/// <param name="popup">게임 진입 때 띄울 공지인지(<c>INoticeService.GetPopups</c>)</param>
		/// <param name="action">게임이 해석하는 동작(예: <c>shop:gem_100</c>). 없으면 null, 1~256자</param>
		/// <exception cref="NoticeDocumentException">값이 규칙에 맞지 않는 경우</exception>
		public NoticeItem(string id, IReadOnlyDictionary<string, NoticeContent> contents, string category = null, int priority = 0,
			DateTime? startsAt = null, DateTime? endsAt = null, IEnumerable<string> platforms = null, bool popup = false, string action = null)
		{
			if (!NoticeNames.IsId(id))
			{
				throw NoticeJson.Invalid($"Notice id must be 1-{NoticeNames.MaxIdLength} letters, digits, '.', '_' or '-': '{id}'");
			}

			category = category ?? DefaultCategory;

			if (!NoticeNames.IsCategory(category))
			{
				throw NoticeJson.Invalid($"Notice '{id}' category must be 1-{NoticeNames.MaxCategoryLength} letters, digits, '.', '_' or '-': '{category}'");
			}

			if ((startsAt.HasValue && startsAt.Value.Kind != DateTimeKind.Utc) || (endsAt.HasValue && endsAt.Value.Kind != DateTimeKind.Utc))
			{
				throw NoticeJson.Invalid($"Notice '{id}' times must be UTC");
			}

			if (startsAt.HasValue && endsAt.HasValue && endsAt.Value <= startsAt.Value)
			{
				throw NoticeJson.Invalid($"Notice '{id}' endsAt must be later than startsAt");
			}

			if (action != null && (action.Length == 0 || action.Length > NoticeNames.MaxActionLength))
			{
				throw NoticeJson.Invalid($"Notice '{id}' action must be 1-{NoticeNames.MaxActionLength} characters");
			}

			Id = id;
			Category = category;
			Priority = priority;
			StartsAt = startsAt;
			EndsAt = endsAt;
			Platforms = CopyPlatforms(id, platforms);
			Popup = popup;
			Action = action;
			Contents = CopyContents(id, contents);
		}

		public string Id { get; }

		public string Category { get; }

		public int Priority { get; }

		public DateTime? StartsAt { get; }

		public DateTime? EndsAt { get; }

		/// <summary>보일 플랫폼 이름입니다. 비어 있으면 모든 플랫폼입니다.</summary>
		public IReadOnlyList<string> Platforms { get; }

		public bool Popup { get; }

		public string Action { get; }

		/// <summary>비교용 언어 태그(소문자, <c>-</c> 구분) → 내용입니다.</summary>
		public IReadOnlyDictionary<string, NoticeContent> Contents { get; }

		/// <summary><paramref name="utcNow"/> 가 보이는 기간(<see cref="StartsAt"/> 포함, <see cref="EndsAt"/> 제외) 안인지입니다.</summary>
		/// <exception cref="ArgumentException"><paramref name="utcNow"/> 가 UTC 가 아닌 경우</exception>
		public bool IsActiveAt(DateTime utcNow)
		{
			if (utcNow.Kind != DateTimeKind.Utc)
			{
				throw new ArgumentException("Time must be UTC", nameof(utcNow));
			}

			return (!StartsAt.HasValue || StartsAt.Value <= utcNow) && (!EndsAt.HasValue || utcNow < EndsAt.Value);
		}

		/// <summary>이 플랫폼에서 보이는지입니다.</summary>
		public bool IsFor(string platform)
		{
			if (Platforms.Count == 0)
			{
				return true;
			}

			foreach (var name in Platforms)
			{
				if (string.Equals(name, platform, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// 언어에 맞는 내용입니다. <paramref name="language"/>(예: <c>zh-Hant-TW</c>) → 뒤 부분을 하나씩 뺀 태그(<c>zh-Hant</c>, <c>zh</c>) → <paramref name="fallbackLanguage"/> 순서로 찾고,
		/// 없으면 null 입니다. 대소문자와 <c>_</c>/<c>-</c> 를 가리지 않습니다.
		/// </summary>
		/// <exception cref="ArgumentException">언어 태그가 규칙에 맞지 않는 경우</exception>
		public NoticeContent FindContent(string language, string fallbackLanguage = null)
		{
			var tag = NoticeNames.NormalizeLanguage(language) ?? throw new ArgumentException($"Invalid language tag '{language}'", nameof(language));

			while (true)
			{
				if (Contents.TryGetValue(tag, out var content))
				{
					return content;
				}

				var separator = tag.LastIndexOf('-');

				if (separator < 0)
				{
					break;
				}

				tag = tag.Substring(0, separator);
			}

			if (fallbackLanguage == null)
			{
				return null;
			}

			var fallback = NoticeNames.NormalizeLanguage(fallbackLanguage) ??
				throw new ArgumentException($"Invalid language tag '{fallbackLanguage}'", nameof(fallbackLanguage));
			return Contents.TryGetValue(fallback, out var fallbackContent) ? fallbackContent : null;
		}

		private static IReadOnlyList<string> CopyPlatforms(string id, IEnumerable<string> platforms)
		{
			if (platforms == null)
			{
				return AllPlatforms;
			}

			var copied = new List<string>();

			foreach (var platform in platforms)
			{
				if (!NoticeNames.IsPlatform(platform))
				{
					throw NoticeJson.Invalid(
						$"Notice '{id}' platform must be 1-{NoticeNames.MaxPlatformLength} lowercase letters, digits, '_' or '-': '{platform}'");
				}

				if (copied.Contains(platform))
				{
					throw NoticeJson.Invalid($"Notice '{id}' lists platform '{platform}' twice");
				}

				copied.Add(platform);
			}

			if (copied.Count == 0)
			{
				throw NoticeJson.Invalid($"Notice '{id}' platforms is empty (leave it out to show on every platform)");
			}

			return copied.AsReadOnly();
		}

		private static IReadOnlyDictionary<string, NoticeContent> CopyContents(string id, IReadOnlyDictionary<string, NoticeContent> contents)
		{
			if (contents == null || contents.Count == 0)
			{
				throw NoticeJson.Invalid($"Notice '{id}' needs at least one content");
			}

			var copied = new Dictionary<string, NoticeContent>(StringComparer.Ordinal);

			foreach (var pair in contents)
			{
				var language = NoticeNames.NormalizeLanguage(pair.Key) ??
					throw NoticeJson.Invalid($"Notice '{id}' has an invalid language tag '{pair.Key}'");

				if (pair.Value == null)
				{
					throw NoticeJson.Invalid($"Notice '{id}' content '{pair.Key}' is null");
				}

				if (copied.ContainsKey(language))
				{
					throw NoticeJson.Invalid($"Notice '{id}' has language '{pair.Key}' twice (tags ignore case and '_'/'-')");
				}

				copied.Add(language, pair.Value);
			}

			return new ReadOnlyDictionary<string, NoticeContent>(copied);
		}
	}
}
