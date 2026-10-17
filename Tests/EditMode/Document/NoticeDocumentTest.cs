using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice.Tests
{
	/// <summary>
	/// 문서 형식 테스트입니다. Unity EditMode 와 .NET(<c>Server~/tests/Geuneda.Notice.Document.Tests</c>)이 같은 파일을 돌려 두 런타임의 판정이 같은지 봅니다.
	/// 그래서 공개 API 와 NUnit 3 의 <c>Assert.That</c> 제약식만 씁니다.
	/// </summary>
	[TestFixture]
	public class NoticeDocumentTest
	{
		private const string Full = @"{
  ""defaultLanguage"": ""EN"",
  ""notices"": [
    {
      ""id"": ""update-2.1"", ""category"": ""event"", ""priority"": 10, ""popup"": true, ""action"": ""shop:gem_100"",
      ""startsAt"": ""2026-10-17T09:00:00+09:00"", ""endsAt"": ""2026-10-31T00:00:00.5Z"", ""platforms"": [""android"", ""ios""],
      ""contents"": {
        ""ko"": { ""title"": ""업데이트"", ""body"": ""본문"", ""linkUrl"": ""https://example.com/a?b=1"", ""linkLabel"": ""자세히"" },
        ""zh_Hant"": { ""title"": ""更新"" }
      }
    }
  ]
}";

		private static NoticeDocument Parse(string json, bool strict = false)
		{
			return NoticeDocument.Parse(Encoding.UTF8.GetBytes(json), strict);
		}

		private static string One(string item)
		{
			return "{\"notices\":[" + item + "]}";
		}

		private static string Item(string extra = "", string contents = "{\"ko\":{\"title\":\"t\"}}")
		{
			return "{\"id\":\"n1\"" + extra + ",\"contents\":" + contents + "}";
		}

		private static void AssertInvalid(string json, string messagePart, bool strict = false)
		{
			Assert.That(() => Parse(json, strict), Throws.TypeOf<NoticeDocumentException>().With.Message.Contains(messagePart));
		}

		private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
		{
			return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
		}

		[Test]
		public void Parse_FullItem_ReadsEveryField()
		{
			var document = Parse(Full, true);
			var item = document.Items.Single();

			Assert.That(document.DefaultLanguage, Is.EqualTo("en"));
			Assert.That(item.Id, Is.EqualTo("update-2.1"));
			Assert.That(item.Category, Is.EqualTo("event"));
			Assert.That(item.Priority, Is.EqualTo(10));
			Assert.That(item.Popup, Is.True);
			Assert.That(item.Action, Is.EqualTo("shop:gem_100"));
			Assert.That(item.StartsAt, Is.EqualTo(Utc(2026, 10, 17)));
			Assert.That(item.StartsAt.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
			Assert.That(item.EndsAt, Is.EqualTo(Utc(2026, 10, 31).AddMilliseconds(500)));
			Assert.That(item.Platforms, Is.EqualTo(new[] { "android", "ios" }));
			Assert.That(item.Contents.Keys.OrderBy(k => k, StringComparer.Ordinal), Is.EqualTo(new[] { "ko", "zh-hant" }));

			var ko = item.Contents["ko"];
			Assert.That(ko.Title, Is.EqualTo("업데이트"));
			Assert.That(ko.Body, Is.EqualTo("본문"));
			Assert.That(ko.LinkUrl, Is.EqualTo("https://example.com/a?b=1"));
			Assert.That(ko.LinkLabel, Is.EqualTo("자세히"));
		}

		[Test]
		public void Parse_MinimalItem_UsesDefaults()
		{
			var item = Parse(One(Item())).Items.Single();

			Assert.That(item.Category, Is.EqualTo(NoticeItem.DefaultCategory));
			Assert.That(item.Priority, Is.EqualTo(0));
			Assert.That(item.Popup, Is.False);
			Assert.That(item.Action, Is.Null);
			Assert.That(item.StartsAt, Is.Null);
			Assert.That(item.EndsAt, Is.Null);
			Assert.That(item.Platforms, Is.Empty);
			Assert.That(item.IsFor("anything"), Is.True);
			Assert.That(item.Contents["ko"].Body, Is.Null);
		}

		[Test]
		public void Parse_JsonNull_IsMissing()
		{
			var item = Parse(One(Item(",\"category\":null,\"endsAt\":null,\"platforms\":null,\"popup\":null"))).Items.Single();

			Assert.That(item.Category, Is.EqualTo(NoticeItem.DefaultCategory));
			Assert.That(item.EndsAt, Is.Null);
			Assert.That(item.Platforms, Is.Empty);
		}

		[Test]
		public void Parse_EmptyList_IsValid()
		{
			Assert.That(Parse("{\"notices\":[]}").Items, Is.Empty);
		}

		[Test]
		public void Parse_Utf8Bom_IsAccepted()
		{
			var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(One(Item()))).ToArray();
			Assert.That(NoticeDocument.Parse(bytes).Items.Count, Is.EqualTo(1));
		}

		[Test]
		public void Parse_BrokenJson_IsRejected()
		{
			AssertInvalid("{\"notices\":[]", "not valid JSON");
			AssertInvalid("{\"notices\":[]} {}", "Notice document");
			AssertInvalid("[]", "must be a JSON object");
			AssertInvalid("{\"notices\":[],\"notices\":[]}", "not valid JSON");
			AssertInvalid("{}", "notices is required");
			AssertInvalid("{\"notices\":{}}", "must be an array");
			AssertInvalid("{\"notices\":[1]}", "notices[0] must be an object");
			Assert.That(() => NoticeDocument.Parse(new byte[] { 0x7B, 0xFF, 0x7D }), Throws.TypeOf<NoticeDocumentException>().With.Message.Contains("UTF-8"));
		}

		[Test]
		public void Parse_InvalidIds_AreRejected()
		{
			AssertInvalid(One("{\"contents\":{\"ko\":{\"title\":\"t\"}}}"), "id is required");
			AssertInvalid(One("{\"id\":\"a b\",\"contents\":{\"ko\":{\"title\":\"t\"}}}"), "Notice id must be");
			AssertInvalid(One("{\"id\":\"" + new string('a', 129) + "\",\"contents\":{\"ko\":{\"title\":\"t\"}}}"), "Notice id must be");
			AssertInvalid("{\"notices\":[" + Item() + "," + Item() + "]}", "appears twice");
			AssertInvalid(One(Item(",\"category\":\"bad category\"")), "category must be");
		}

		[Test]
		public void Parse_InvalidValues_AreRejected()
		{
			AssertInvalid(One(Item(",\"priority\":1.5")), "priority must be an integer");
			AssertInvalid(One(Item(",\"priority\":3000000000")), "priority is out of range");
			AssertInvalid(One(Item(",\"popup\":\"yes\"")), "popup must be true or false");
			AssertInvalid(One(Item(",\"action\":\"\"")), "action must be");
			AssertInvalid(One(Item(",\"action\":\"" + new string('a', 257) + "\"")), "action must be");
			AssertInvalid(One(Item(",\"id2\":1,\"category\":5")), "category must be a string");
		}

		[Test]
		public void Parse_InvalidTimes_AreRejected()
		{
			AssertInvalid(One(Item(",\"startsAt\":\"2026-10-17T00:00:00\"")), "with Z or an offset");
			AssertInvalid(One(Item(",\"startsAt\":\"2026-10-17\"")), "with Z or an offset");
			AssertInvalid(One(Item(",\"startsAt\":\"2026-10-17T00:00:00Z\",\"endsAt\":\"2026-10-17T00:00:00Z\"")), "endsAt must be later");
			AssertInvalid(One(Item(",\"startsAt\":5")), "startsAt must be a string");
		}

		[Test]
		public void Parse_InvalidPlatforms_AreRejected()
		{
			AssertInvalid(One(Item(",\"platforms\":[]")), "platforms is empty");
			AssertInvalid(One(Item(",\"platforms\":[\"Android\"]")), "platform must be");
			AssertInvalid(One(Item(",\"platforms\":[\"ios\",\"ios\"]")), "twice");
			AssertInvalid(One(Item(",\"platforms\":[1]")), "platforms must be strings");
			AssertInvalid(One(Item(",\"platforms\":\"ios\"")), "platforms must be an array");
		}

		[Test]
		public void Parse_InvalidContents_AreRejected()
		{
			AssertInvalid(One("{\"id\":\"n1\"}"), "contents is required");
			AssertInvalid(One(Item("", "{}")), "at least one content");
			AssertInvalid(One(Item("", "{\"ko\":{}}")), "title is required");
			AssertInvalid(One(Item("", "{\"ko\":{\"title\":\"\"}}")), "title is required");
			AssertInvalid(One(Item("", "{\"ko\":\"t\"}")), "contents.ko must be an object");
			AssertInvalid(One(Item("", "{\"k o\":{\"title\":\"t\"}}")), "invalid language tag");
			AssertInvalid(One(Item("", "{\"-ko\":{\"title\":\"t\"}}")), "invalid language tag");
			AssertInvalid(One(Item("", "{\"zh--tw\":{\"title\":\"t\"}}")), "invalid language tag");
			AssertInvalid(One(Item("", "{\"zh-TW\":{\"title\":\"a\"},\"zh_tw\":{\"title\":\"b\"}}")), "twice");
			AssertInvalid("{\"defaultLanguage\":\"e n\",\"notices\":[]}", "defaultLanguage is not a valid language tag");
		}

		[Test]
		public void Parse_LinksMustBeHttp()
		{
			Assert.That(Parse(One(Item("", "{\"ko\":{\"title\":\"t\",\"linkUrl\":\"http://example.com\"}}"))).Items.Count, Is.EqualTo(1));

			foreach (var link in new[] { "javascript:alert(1)", "file:///etc/passwd", "/relative/path", "example.com", "game://shop" })
			{
				AssertInvalid(One(Item("", "{\"ko\":{\"title\":\"t\",\"linkUrl\":\"" + link + "\"}}")), "absolute http or https URL");
			}
		}

		[Test]
		public void Parse_UnknownProperties_IgnoredUnlessStrict()
		{
			var lenient = "{\"version\":2,\"notices\":[{\"id\":\"n1\",\"startAt\":\"x\",\"contents\":{\"ko\":{\"title\":\"t\",\"image\":\"i\"}}}]}";
			Assert.That(Parse(lenient).Items.Single().StartsAt, Is.Null);

			AssertInvalid("{\"version\":2,\"notices\":[]}", "document has unknown property 'version'", true);
			AssertInvalid(One(Item(",\"startAt\":\"2026-10-17T00:00:00Z\"")), "notices[0] has unknown property 'startAt'", true);
			AssertInvalid(One(Item("", "{\"ko\":{\"title\":\"t\",\"image\":\"i\"}}")), "notices[0].contents.ko has unknown property 'image'", true);
		}

		[Test]
		public void FindContent_FallsBackThroughShorterTagsThenDefault()
		{
			var item = Parse("{\"notices\":[" + Item("", "{\"zh-Hant\":{\"title\":\"hant\"},\"zh\":{\"title\":\"zh\"},\"en\":{\"title\":\"en\"}}") + "]}").Items.Single();

			Assert.That(item.FindContent("zh_Hant_TW").Title, Is.EqualTo("hant"));
			Assert.That(item.FindContent("ZH-HANT").Title, Is.EqualTo("hant"));
			Assert.That(item.FindContent("zh-Hans").Title, Is.EqualTo("zh"));
			Assert.That(item.FindContent("ko"), Is.Null);
			Assert.That(item.FindContent("ko", "EN").Title, Is.EqualTo("en"));
			Assert.That(item.FindContent("ko", "ja"), Is.Null);
			Assert.That(() => item.FindContent("k o"), Throws.TypeOf<ArgumentException>());
			Assert.That(() => item.FindContent(null), Throws.TypeOf<ArgumentException>());
		}

		[Test]
		public void IsActiveAt_IncludesStartExcludesEnd()
		{
			var item = Parse(One(Item(",\"startsAt\":\"2026-10-17T00:00:00Z\",\"endsAt\":\"2026-10-18T00:00:00Z\""))).Items.Single();

			Assert.That(item.IsActiveAt(Utc(2026, 10, 16, 23, 59, 59)), Is.False);
			Assert.That(item.IsActiveAt(Utc(2026, 10, 17)), Is.True);
			Assert.That(item.IsActiveAt(Utc(2026, 10, 17, 23, 59, 59)), Is.True);
			Assert.That(item.IsActiveAt(Utc(2026, 10, 18)), Is.False);
			Assert.That(() => item.IsActiveAt(new DateTime(2026, 10, 17, 1, 0, 0, DateTimeKind.Local)), Throws.TypeOf<ArgumentException>());
			Assert.That(() => item.IsActiveAt(new DateTime(2026, 10, 17, 1, 0, 0, DateTimeKind.Unspecified)), Throws.TypeOf<ArgumentException>());
		}

		[Test]
		public void ActiveAt_KeepsOnlyVisibleItemsInOrder()
		{
			var document = Parse("{\"defaultLanguage\":\"en\",\"notices\":[" +
				"{\"id\":\"past\",\"endsAt\":\"2026-10-17T00:00:00Z\",\"contents\":{\"ko\":{\"title\":\"t\"}}}," +
				"{\"id\":\"always\",\"contents\":{\"ko\":{\"title\":\"t\"}}}," +
				"{\"id\":\"future\",\"startsAt\":\"2026-10-18T00:00:00Z\",\"contents\":{\"ko\":{\"title\":\"t\"}}}," +
				"{\"id\":\"now\",\"startsAt\":\"2026-10-17T00:00:00Z\",\"contents\":{\"ko\":{\"title\":\"t\"}}}]}");

			var active = document.ActiveAt(Utc(2026, 10, 17));

			Assert.That(active.Items.Select(i => i.Id), Is.EqualTo(new[] { "always", "now" }));
			Assert.That(active.DefaultLanguage, Is.EqualTo("en"));
		}

		[Test]
		public void ActiveAt_NonUtc_ThrowsEvenWhenEmpty()
		{
			var empty = new NoticeDocument(new NoticeItem[0]);

			Assert.That(() => empty.ActiveAt(new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Local)), Throws.TypeOf<ArgumentException>());
			Assert.That(() => empty.ActiveAt(new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Unspecified)), Throws.TypeOf<ArgumentException>());
			Assert.That(empty.ActiveAt(Utc(2026, 10, 17)).Items, Is.Empty);
		}

		[Test]
		public void ToUtf8Json_RoundTripsAndIsDeterministic()
		{
			var document = Parse(Full);
			var bytes = document.ToUtf8Json();
			var again = NoticeDocument.Parse(bytes, true);

			Assert.That(again.ToUtf8Json(), Is.EqualTo(bytes));
			Assert.That(bytes.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));

			var text = Encoding.UTF8.GetString(bytes);
			Assert.That(text, Does.Contain("\"startsAt\":\"2026-10-17T00:00:00Z\""));
			Assert.That(text, Does.Contain("\"endsAt\":\"2026-10-31T00:00:00.5Z\""));
			Assert.That(text.IndexOf("\"ko\"", StringComparison.Ordinal), Is.LessThan(text.IndexOf("\"zh-hant\"", StringComparison.Ordinal)));

			var item = again.Items.Single();
			Assert.That(item.Platforms, Is.EqualTo(new[] { "android", "ios" }));
			Assert.That(item.Contents["zh-hant"].Title, Is.EqualTo("更新"));
			Assert.That(item.Contents["ko"].LinkLabel, Is.EqualTo("자세히"));
		}

		[Test]
		public void ToUtf8Json_OmitsDefaults()
		{
			var text = Encoding.UTF8.GetString(Parse(One(Item())).ToUtf8Json());
			Assert.That(text, Is.EqualTo("{\"notices\":[{\"id\":\"n1\",\"contents\":{\"ko\":{\"title\":\"t\"}}}]}"));
		}

		[Test]
		public void Constructors_ValidateValues()
		{
			var contents = new Dictionary<string, NoticeContent> { ["ko"] = new NoticeContent("t") };

			Assert.That(() => new NoticeContent(null), Throws.TypeOf<NoticeDocumentException>());
			Assert.That(() => new NoticeContent("t", linkUrl: "ftp://example.com"), Throws.TypeOf<NoticeDocumentException>());
			Assert.That(() => new NoticeItem("n1", null), Throws.TypeOf<NoticeDocumentException>());
			Assert.That(() => new NoticeItem("n1", new Dictionary<string, NoticeContent> { ["ko"] = null }), Throws.TypeOf<NoticeDocumentException>());
			Assert.That(() => new NoticeItem("n1", contents, startsAt: new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Local)),
				Throws.TypeOf<NoticeDocumentException>().With.Message.Contains("UTC"));
			Assert.That(() => new NoticeDocument(null), Throws.TypeOf<NoticeDocumentException>());
			Assert.That(() => new NoticeDocument(new NoticeItem[] { null }), Throws.TypeOf<NoticeDocumentException>());

			var item = new NoticeItem("n1", contents, platforms: new[] { NoticePlatforms.Ios });
			Assert.That(item.IsFor(NoticePlatforms.Ios), Is.True);
			Assert.That(item.IsFor(NoticePlatforms.Android), Is.False);
		}

		[Test]
		public void Constructors_CopyInputs()
		{
			var contents = new Dictionary<string, NoticeContent> { ["ko"] = new NoticeContent("t") };
			var platforms = new List<string> { "ios" };
			var items = new List<NoticeItem> { new NoticeItem("n1", contents, platforms: platforms) };
			var document = new NoticeDocument(items);

			contents["en"] = new NoticeContent("e");
			platforms.Add("android");
			items.Clear();

			Assert.That(document.Items.Count, Is.EqualTo(1));
			Assert.That(document.Items[0].Contents.Count, Is.EqualTo(1));
			Assert.That(document.Items[0].Platforms, Is.EqualTo(new[] { "ios" }));
		}
	}
}
