using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>
	/// 공지 문서입니다. 모든 언어·플랫폼의 공지를 한 문서에 담습니다. 만든 뒤 바뀌지 않습니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>
	/// <para>형식(UTF-8 JSON, 이름은 camelCase, 시각은 Z 나 오프셋이 있는 ISO 8601):</para>
	/// <code>
	/// {
	///   "defaultLanguage": "en",
	///   "notices": [
	///     {
	///       "id": "update-2-1", "category": "notice", "priority": 10, "popup": true, "action": "shop:gem_100",
	///       "startsAt": "2026-10-17T00:00:00Z", "endsAt": "2026-10-31T00:00:00Z", "platforms": ["android", "ios"],
	///       "contents": { "ko": { "title": "...", "body": "...", "linkUrl": "https://...", "linkLabel": "..." }, "en": { "title": "..." } }
	///     }
	///   ]
	/// }
	/// </code>
	/// <para>
	/// 같은 이름의 속성, 뒤에 붙은 내용, 시간대 없는 시각은 거절합니다. 모르는 속성은 클라이언트가 무시하고(다음 판의 서버가 속성을 더해도 읽음),
	/// 엄격한 읽기(서버, <c>strict</c>)는 거절합니다(<c>startAt</c> 같은 오타로 기간이 빠진 공지가 끝없이 보이지 않게).
	/// </para>
	/// </remarks>
	public sealed class NoticeDocument
	{
		private static readonly string[] DocumentProperties = { "defaultLanguage", "notices" };

		private static readonly string[] ItemProperties =
			{ "id", "category", "priority", "startsAt", "endsAt", "platforms", "popup", "action", "contents" };

		private static readonly string[] ContentProperties = { "title", "body", "linkUrl", "linkLabel" };

		/// <param name="items">공지 목록. 순서는 정렬이 같을 때의 순서입니다. ID 가 겹치면 안 됩니다</param>
		/// <param name="defaultLanguage">공지에 기기 언어 내용이 없을 때 쓸 언어 태그. 없으면 null</param>
		/// <exception cref="NoticeDocumentException">목록이 null 이거나 null 항목·겹친 ID 가 있거나 언어 태그가 규칙에 맞지 않는 경우</exception>
		public NoticeDocument(IEnumerable<NoticeItem> items, string defaultLanguage = null)
		{
			if (items == null)
			{
				throw NoticeJson.Invalid("Notice list is null");
			}

			if (defaultLanguage != null && NoticeNames.NormalizeLanguage(defaultLanguage) == null)
			{
				throw NoticeJson.Invalid($"defaultLanguage is not a valid language tag: '{defaultLanguage}'");
			}

			var copied = new List<NoticeItem>();
			var ids = new HashSet<string>(StringComparer.Ordinal);

			foreach (var item in items)
			{
				if (item == null)
				{
					throw NoticeJson.Invalid("Notice list has a null item");
				}

				if (!ids.Add(item.Id))
				{
					throw NoticeJson.Invalid($"Notice id '{item.Id}' appears twice");
				}

				copied.Add(item);
			}

			Items = copied.AsReadOnly();
			DefaultLanguage = defaultLanguage == null ? null : NoticeNames.NormalizeLanguage(defaultLanguage);
		}

		/// <summary>공지 목록입니다(문서 순서).</summary>
		public IReadOnlyList<NoticeItem> Items { get; }

		/// <summary>비교용(소문자, <c>-</c> 구분) 기본 언어 태그입니다. 없으면 null 입니다.</summary>
		public string DefaultLanguage { get; }

		/// <summary>UTF-8 JSON 문서를 읽습니다.</summary>
		/// <param name="strict">모르는 속성을 거절할지. 서버·운영 도구는 true, 클라이언트는 false</param>
		/// <exception cref="ArgumentNullException"><paramref name="utf8"/> 이 null 인 경우</exception>
		/// <exception cref="NoticeDocumentException">형식이나 규칙에 맞지 않는 경우</exception>
		public static NoticeDocument Parse(byte[] utf8, bool strict = false)
		{
			return FromJson(NoticeJson.ParseObject(utf8), strict);
		}

		/// <inheritdoc cref="Parse(byte[], bool)"/>
		public static NoticeDocument Parse(string json, bool strict = false)
		{
			return FromJson(NoticeJson.ParseObject(json), strict);
		}

		/// <summary>
		/// <paramref name="utcNow"/> 에 보이는 기간인 공지만 남긴 문서입니다. 서버가 아직 시작하지 않은 공지(미리 알리지 않을 내용)와 끝난 공지를 빼고 내려줄 때 씁니다.
		/// </summary>
		/// <exception cref="ArgumentException"><paramref name="utcNow"/> 가 UTC 가 아닌 경우</exception>
		public NoticeDocument ActiveAt(DateTime utcNow)
		{
			if (utcNow.Kind != DateTimeKind.Utc)
			{
				throw new ArgumentException("Time must be UTC", nameof(utcNow));
			}

			return new NoticeDocument(Items.Where(item => item.IsActiveAt(utcNow)), DefaultLanguage);
		}

		/// <summary>
		/// UTF-8 JSON(BOM 없음, 들여쓰기 없음)으로 씁니다. 같은 문서는 늘 같은 바이트입니다(언어는 이름 순, 기본값인 속성은 뺌).
		/// <see cref="Parse(byte[], bool)"/> 가 같은 문서로 읽습니다(언어 태그는 비교용 모양으로).
		/// </summary>
		public byte[] ToUtf8Json()
		{
			var root = new JObject();

			if (DefaultLanguage != null)
			{
				root["defaultLanguage"] = DefaultLanguage;
			}

			var notices = new JArray();

			foreach (var item in Items)
			{
				notices.Add(ToJson(item));
			}

			root["notices"] = notices;
			return new UTF8Encoding(false).GetBytes(root.ToString(Formatting.None));
		}

		private static JObject ToJson(NoticeItem item)
		{
			var json = new JObject { ["id"] = item.Id };

			if (item.Category != NoticeItem.DefaultCategory)
			{
				json["category"] = item.Category;
			}

			if (item.Priority != 0)
			{
				json["priority"] = item.Priority;
			}

			if (item.StartsAt.HasValue)
			{
				json["startsAt"] = NoticeJson.FormatUtc(item.StartsAt.Value);
			}

			if (item.EndsAt.HasValue)
			{
				json["endsAt"] = NoticeJson.FormatUtc(item.EndsAt.Value);
			}

			if (item.Platforms.Count > 0)
			{
				json["platforms"] = new JArray(item.Platforms.Cast<object>().ToArray());
			}

			if (item.Popup)
			{
				json["popup"] = true;
			}

			if (item.Action != null)
			{
				json["action"] = item.Action;
			}

			var contents = new JObject();

			foreach (var language in item.Contents.Keys.OrderBy(key => key, StringComparer.Ordinal))
			{
				var content = item.Contents[language];
				var value = new JObject { ["title"] = content.Title };

				if (content.Body != null)
				{
					value["body"] = content.Body;
				}

				if (content.LinkUrl != null)
				{
					value["linkUrl"] = content.LinkUrl;
				}

				if (content.LinkLabel != null)
				{
					value["linkLabel"] = content.LinkLabel;
				}

				contents[language] = value;
			}

			json["contents"] = contents;
			return json;
		}

		private static NoticeDocument FromJson(JObject root, bool strict)
		{
			if (strict)
			{
				NoticeJson.RejectUnknown(root, "document", DocumentProperties);
			}

			var defaultLanguage = NoticeJson.OptionalString(root, "defaultLanguage", "document");
			var notices = NoticeJson.RequireArray(root, "notices", "document");
			var items = new List<NoticeItem>(notices.Count);

			for (var i = 0; i < notices.Count; i++)
			{
				items.Add(ItemFromJson(NoticeJson.RequireObject(notices[i], $"notices[{i}]"), $"notices[{i}]", strict));
			}

			return new NoticeDocument(items, defaultLanguage);
		}

		private static NoticeItem ItemFromJson(JObject json, string path, bool strict)
		{
			if (strict)
			{
				NoticeJson.RejectUnknown(json, path, ItemProperties);
			}

			var id = NoticeJson.RequireString(json, "id", path);
			var platformsJson = NoticeJson.OptionalArray(json, "platforms", path);
			List<string> platforms = null;

			if (platformsJson != null)
			{
				platforms = new List<string>(platformsJson.Count);

				foreach (var platform in platformsJson)
				{
					platforms.Add(platform.Type == JTokenType.String ? (string) platform : throw NoticeJson.Invalid($"{path}.platforms must be strings"));
				}
			}

			var contentsJson = NoticeJson.RequireObject(json, "contents", path);
			var contents = new Dictionary<string, NoticeContent>(StringComparer.Ordinal);

			foreach (var property in contentsJson.Properties())
			{
				var contentPath = $"{path}.contents.{property.Name}";
				var value = NoticeJson.RequireObject(property.Value, contentPath);

				if (strict)
				{
					NoticeJson.RejectUnknown(value, contentPath, ContentProperties);
				}

				var title = NoticeJson.RequireString(value, "title", contentPath);
				var body = NoticeJson.OptionalString(value, "body", contentPath);
				var linkUrl = NoticeJson.OptionalString(value, "linkUrl", contentPath);
				var linkLabel = NoticeJson.OptionalString(value, "linkLabel", contentPath);

				try
				{
					contents[property.Name] = new NoticeContent(title, body, linkUrl, linkLabel);
				}
				catch (NoticeDocumentException exception)
				{
					throw NoticeJson.Invalid($"{contentPath}: {exception.Message}");
				}
			}

			var category = NoticeJson.OptionalString(json, "category", path);
			var priority = NoticeJson.OptionalInt(json, "priority", path);
			var startsAt = NoticeJson.OptionalUtc(json, "startsAt", path);
			var endsAt = NoticeJson.OptionalUtc(json, "endsAt", path);
			var popup = NoticeJson.OptionalBool(json, "popup", path);
			var action = NoticeJson.OptionalString(json, "action", path);

			try
			{
				return new NoticeItem(id, contents, category, priority, startsAt, endsAt, platforms, popup, action);
			}
			catch (NoticeDocumentException exception)
			{
				throw NoticeJson.Invalid($"{path}: {exception.Message}");
			}
		}
	}
}
