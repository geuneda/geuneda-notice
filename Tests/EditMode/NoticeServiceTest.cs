using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice.Tests
{
	[TestFixture]
	public class NoticeServiceTest
	{
		private static readonly DateTime Now = new DateTime(2026, 10, 17, 3, 0, 0, DateTimeKind.Utc);

		private FakeNoticeServer _server;
		private MemoryStateStore _store;
		private ILogSink _originalSink;
		private List<LogEntry> _logs;

		private sealed class ListSink : ILogSink
		{
			private readonly List<LogEntry> _entries;

			public ListSink(List<LogEntry> entries)
			{
				_entries = entries;
			}

			public void Write(in LogEntry entry)
			{
				_entries.Add(entry);
			}
		}

		[SetUp]
		public void SetUp()
		{
			_originalSink = Log.Sink;
			_logs = new List<LogEntry>();
			Log.Sink = new ListSink(_logs);
			_server = new FakeNoticeServer { Json = Doc(Notice("a")), ETag = "\"e1\"" };
			_store = new MemoryStateStore();
		}

		[TearDown]
		public void TearDown()
		{
			Log.Sink = _originalSink;
		}

		private static string Doc(params string[] notices)
		{
			return "{\"defaultLanguage\":\"en\",\"notices\":[" + string.Join(",", notices) + "]}";
		}

		private static string Notice(string id, string extra = "", string contents = null)
		{
			return "{\"id\":\"" + id + "\"" + extra + ",\"contents\":" + (contents ?? "{\"ko\":{\"title\":\"" + id + "-ko\"},\"en\":{\"title\":\"" + id + "-en\"}}") + "}";
		}

		private NoticeServiceOptions Options()
		{
			return new NoticeServiceOptions
			{
				HttpClient = _server.CreateClient(),
				DocumentUri = FakeNoticeServer.DocumentPath,
				Language = "ko",
				Platform = NoticePlatforms.Android,
				RequestTimeout = TimeSpan.FromSeconds(3),
				DayTimeZone = TimeZoneInfo.CreateCustomTimeZone("test+9", TimeSpan.FromHours(9), "test+9", "test+9")
			};
		}

		private NoticeService CreateService(NoticeServiceOptions options = null)
		{
			return new NoticeService(options ?? Options(), _store);
		}

		private static T Run<T>(UniTask<T> task)
		{
			Assert.IsTrue(task.Status.IsCompleted(), "Expected the task to complete synchronously");
			return task.GetAwaiter().GetResult();
		}

		private NoticeService Loaded(params string[] notices)
		{
			_server.Json = Doc(notices);
			var service = CreateService();
			var result = Run(service.RefreshAsync());
			Assert.IsTrue(result.IsSuccess, result.ToString());
			return service;
		}

		private static string[] Ids(IEnumerable<Notice> notices)
		{
			return notices.Select(n => n.Id).ToArray();
		}

		private int Count(LogLevel level, string contains)
		{
			return _logs.Count(entry => entry.Level == level && (entry.Message ?? entry.Exception?.Message ?? "").Contains(contains));
		}

		[Test]
		public void Constructor_InvalidOptions_Throws()
		{
			Assert.Throws<ArgumentNullException>(() => new NoticeService(null));

			void Expect(Action<NoticeServiceOptions> change)
			{
				var options = Options();
				change(options);
				Assert.Throws<ArgumentException>(() => CreateService(options));
			}

			Expect(o => o.HttpClient = null);
			Expect(o => o.DocumentUri = "");
			Expect(o => o.StateKey = null);
			Expect(o => o.RequestTimeout = TimeSpan.Zero);
			Expect(o => o.Language = null);
			Expect(o => o.Language = "k o");
			Expect(o => o.Platform = "Android");
			Expect(o => o.Platform = "");
		}

		[Test]
		public void Constructor_Defaults()
		{
			var options = Options();
			options.Platform = null;
			options.DayTimeZone = null;
			options.Language = "zh_Hant";
			var service = CreateService(options);

			Assert.AreEqual("zh-hant", service.Language);
			Assert.IsNull(service.Document);
			Assert.IsEmpty(service.GetNotices(Now));
			Assert.AreEqual(0, service.GetUnreadCount(Now));
			Assert.IsEmpty(service.GetPopups(Now));
			Assert.AreEqual(0, _server.Requests.Count);
		}

		[Test]
		public void Refresh_First_AppliesDocumentAndRaisesChanged()
		{
			var service = CreateService();
			var changed = 0;
			service.Changed += () => changed++;

			var result = Run(service.RefreshAsync());

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual(1, changed);
			Assert.AreEqual("a", service.Document.Items.Single().Id);
			Assert.AreEqual("a-ko", service.GetNotices(Now).Single().Title);
			Assert.IsFalse(_server.Requests[0].Headers.ContainsKey("If-None-Match"));
			Assert.AreEqual(TimeSpan.FromSeconds(3), _server.Requests[0].Timeout);
		}

		[Test]
		public void Refresh_Again_SendsIfNoneMatch_And304KeepsDocument()
		{
			var service = CreateService();
			Run(service.RefreshAsync());
			var document = service.Document;
			var changed = 0;
			service.Changed += () => changed++;

			var result = Run(service.RefreshAsync());

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual("\"e1\"", _server.Requests[1].Headers["If-None-Match"]);
			Assert.AreSame(document, service.Document);
			Assert.AreEqual(0, changed);
		}

		[Test]
		public void Refresh_SameBytes_DoesNotRaiseChanged_ButUpdatesETag()
		{
			var service = CreateService();
			Run(service.RefreshAsync());
			var document = service.Document;
			var changed = 0;
			service.Changed += () => changed++;
			_server.ETag = "\"e2\"";

			Assert.IsTrue(Run(service.RefreshAsync()).IsSuccess);
			Assert.AreSame(document, service.Document);
			Assert.AreEqual(0, changed);

			Run(service.RefreshAsync());
			Assert.AreEqual("\"e2\"", _server.Requests[2].Headers["If-None-Match"]);
		}

		[Test]
		public void Refresh_NewDocument_ReplacesAndRaisesChanged()
		{
			var service = CreateService();
			Run(service.RefreshAsync());
			var changed = 0;
			service.Changed += () => changed++;
			_server.Json = Doc(Notice("a"), Notice("b"));
			_server.ETag = "\"e2\"";

			Assert.IsTrue(Run(service.RefreshAsync()).IsSuccess);
			Assert.AreEqual(new[] { "a", "b" }, Ids(service.GetNotices(Now)));
			Assert.AreEqual(1, changed);
		}

		[Test]
		public void Refresh_InvalidDocument_KeepsCurrent()
		{
			var service = CreateService();
			Run(service.RefreshAsync());
			var document = service.Document;
			_server.Json = "{\"notices\":[{\"id\":\"bad id\",\"contents\":{\"ko\":{\"title\":\"t\"}}}]}";
			_server.ETag = "\"e2\"";

			var result = Run(service.RefreshAsync());

			Assert.AreEqual(NoticeResultCodes.InvalidDocument, result.Code);
			Assert.AreSame(document, service.Document);
			Assert.AreEqual(1, Count(LogLevel.Warning, "Rejected notice document"));
		}

		[Test]
		public void Refresh_UnknownProperties_AreIgnored()
		{
			_server.Json = "{\"version\":3,\"notices\":[{\"id\":\"a\",\"banner\":\"x\",\"contents\":{\"ko\":{\"title\":\"t\",\"image\":\"i\"}}}]}";
			var service = CreateService();

			Assert.IsTrue(Run(service.RefreshAsync()).IsSuccess);
			Assert.AreEqual("a", service.GetNotices(Now).Single().Id);
		}

		[Test]
		public void Refresh_Failures_ReturnCodes()
		{
			var service = CreateService();

			_server.StatusCode = 503;
			Assert.AreEqual(HttpResultCodes.HttpError, Run(service.RefreshAsync()).Code);

			_server.StatusCode = 200;
			_server.Failure = Result<HttpResponse>.Fail(ResultCodes.NetworkError, "offline");
			Assert.AreEqual(ResultCodes.NetworkError, Run(service.RefreshAsync()).Code);

			_server.Failure = Result<HttpResponse>.Ok(new HttpResponse(new Uri(FakeNoticeServer.BaseAddress), 304, null, null));
			Assert.AreEqual(HttpResultCodes.HttpError, Run(service.RefreshAsync()).Code, "304 without a conditional request");
			Assert.IsNull(service.Document);
		}

		[Test]
		public void Refresh_Concurrent_SharesOneRequest()
		{
			var service = CreateService();
			_server.Gate = new UniTaskCompletionSource();

			var first = service.RefreshAsync();
			var second = service.RefreshAsync();
			Assert.AreEqual(1, _server.Requests.Count);
			Assert.IsFalse(first.Status.IsCompleted());

			_server.Gate.TrySetResult();

			Assert.IsTrue(Run(first).IsSuccess);
			Assert.IsTrue(Run(second).IsSuccess);
			Assert.IsNotNull(service.Document);

			_server.Gate = null;
			Run(service.RefreshAsync());
			Assert.AreEqual(2, _server.Requests.Count);
		}

		[Test]
		public void Refresh_Cancelled_StopsOnlyThisWait()
		{
			var service = CreateService();
			_server.Gate = new UniTaskCompletionSource();

			using (var cancellation = new CancellationTokenSource())
			{
				var waiting = service.RefreshAsync(cancellation.Token);
				cancellation.Cancel();
				Assert.AreEqual(ResultCodes.Cancelled, Run(waiting).Code);
			}

			_server.Gate.TrySetResult();
			Assert.IsNotNull(service.Document, "The request goes on and its result is applied");

			using (var cancelled = new CancellationTokenSource())
			{
				cancelled.Cancel();
				Assert.AreEqual(ResultCodes.Cancelled, Run(service.RefreshAsync(cancelled.Token)).Code);
			}

			Assert.AreEqual(1, _server.Requests.Count);
		}

		[Test]
		public void GetNotices_FiltersByTimePlatformLanguageAndCategory()
		{
			var service = Loaded(
				Notice("before", ",\"startsAt\":\"2026-10-17T03:00:00.0000001Z\""),
				Notice("starting", ",\"startsAt\":\"2026-10-17T03:00:00Z\""),
				Notice("ending", ",\"endsAt\":\"2026-10-17T03:00:00Z\""),
				Notice("ios", ",\"platforms\":[\"ios\"]"),
				Notice("android", ",\"platforms\":[\"ios\",\"android\"]"),
				Notice("event", ",\"category\":\"event\""),
				Notice("japanese", "", "{\"ja\":{\"title\":\"ja\"}}"),
				Notice("english", "", "{\"en\":{\"title\":\"en\"}}"));

			Assert.AreEqual(new[] { "starting", "android", "event", "english" }, Ids(service.GetNotices(Now)));
			Assert.AreEqual(new[] { "event" }, Ids(service.GetNotices(Now, "event")));
			Assert.AreEqual(new[] { "starting", "android", "english" }, Ids(service.GetNotices(Now, NoticeItem.DefaultCategory)));
			Assert.AreEqual("en", service.GetNotices(Now).Last().Title, "Falls back to the document default language");
			Assert.AreEqual("starting-ko", service.GetNotices(Now)[0].Title);
		}

		[Test]
		public void GetNotices_SortsByPriorityThenLaterStartThenDocumentOrder()
		{
			var service = Loaded(
				Notice("plain1"),
				Notice("early", ",\"startsAt\":\"2026-10-01T00:00:00Z\""),
				Notice("late", ",\"startsAt\":\"2026-10-10T00:00:00Z\""),
				Notice("plain2"),
				Notice("important", ",\"priority\":5"),
				Notice("low", ",\"priority\":-1"));

			Assert.AreEqual(new[] { "important", "late", "early", "plain1", "plain2", "low" }, Ids(service.GetNotices(Now)));
		}

		[Test]
		public void Notice_ExposesItemAndContent()
		{
			var service = Loaded(Notice("a", ",\"category\":\"event\",\"priority\":2,\"popup\":true,\"action\":\"shop:1\",\"endsAt\":\"2026-11-01T00:00:00Z\"",
				"{\"ko\":{\"title\":\"제목\",\"body\":\"본문\",\"linkUrl\":\"https://example.com\",\"linkLabel\":\"보기\"}}"));
			var notice = service.GetNotices(Now).Single();

			Assert.AreEqual("event", notice.Category);
			Assert.AreEqual(2, notice.Priority);
			Assert.IsTrue(notice.Popup);
			Assert.AreEqual("shop:1", notice.Action);
			Assert.IsNull(notice.StartsAt);
			Assert.AreEqual(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), notice.EndsAt);
			Assert.AreEqual("제목", notice.Title);
			Assert.AreEqual("본문", notice.Body);
			Assert.AreEqual("https://example.com", notice.LinkUrl);
			Assert.AreEqual("보기", notice.LinkLabel);
			Assert.AreSame(service.Document.Items[0], notice.Item);
		}

		[Test]
		public void Language_ChangeRaisesChangedAndSwitchesContent()
		{
			var service = Loaded(Notice("a"));
			var changed = 0;
			service.Changed += () => changed++;

			service.Language = "KO";
			Assert.AreEqual(0, changed, "Same tag ignoring case");

			service.Language = "en-US";
			Assert.AreEqual(1, changed);
			Assert.AreEqual("en-us", service.Language);
			Assert.AreEqual("a-en", service.GetNotices(Now).Single().Title);

			Assert.Throws<ArgumentException>(() => service.Language = null);
			Assert.Throws<ArgumentException>(() => service.Language = "e n");
			Assert.AreEqual("en-us", service.Language);
		}

		[Test]
		public void MarkRead_CountsUnreadAndPersists()
		{
			var service = Loaded(Notice("a"), Notice("b"), Notice("c", ",\"category\":\"event\""));
			var changed = 0;
			service.Changed += () => changed++;

			Assert.AreEqual(3, service.GetUnreadCount(Now));
			Assert.AreEqual(1, service.GetUnreadCount(Now, "event"));

			service.MarkRead("a");
			service.MarkRead("a");
			service.MarkRead("c");

			Assert.AreEqual(2, changed);
			Assert.IsTrue(service.IsRead("a"));
			Assert.IsFalse(service.IsRead("b"));
			Assert.IsFalse(service.IsRead(null));
			Assert.AreEqual(1, service.GetUnreadCount(Now));
			Assert.AreEqual(0, service.GetUnreadCount(Now, "event"));

			var restarted = CreateService();
			Assert.IsTrue(restarted.IsRead("a"));
			Assert.IsTrue(restarted.IsRead("c"));
			Assert.IsFalse(restarted.IsRead("b"));
		}

		[Test]
		public void MarkRead_KeepsTheNewestRecordsUpToTheCap()
		{
			var service = Loaded(Notice("a"));

			service.MarkRead("from-web");
			Assert.IsTrue(service.IsRead("from-web"), "Ids that are not in the document are recorded too");

			for (var i = 1; i < NoticeState.MaxReadRecords; i++)
			{
				service.MarkRead("n" + i);
			}

			Assert.IsTrue(service.IsRead("from-web"));

			service.MarkRead("last");

			Assert.IsFalse(service.IsRead("from-web"), "The oldest record goes first");
			Assert.IsTrue(service.IsRead("n1"));
			Assert.IsTrue(service.IsRead("last"));

			var restarted = CreateService();
			Assert.IsFalse(restarted.IsRead("from-web"));
			Assert.IsTrue(restarted.IsRead("n1"));
			Assert.IsTrue(restarted.IsRead("last"));
		}

		[Test]
		public void Refresh_DocumentWithoutAnId_KeepsItsRecords()
		{
			// 오래된 CDN 사본·잠시 내린 공지·빈 문서 한 번에 읽음과 오늘 안 보기를 잃지 않는다
			var service = Loaded(Notice("a"), Notice("b", ",\"popup\":true"));
			service.MarkRead("b");
			service.SnoozeForToday("b", Now);
			var writes = _store.Writes;

			foreach (var stale in new[] { Doc(Notice("a")), Doc() })
			{
				_server.Json = stale;
				_server.ETag = null;
				Assert.IsTrue(Run(service.RefreshAsync()).IsSuccess);
			}

			Assert.AreEqual(writes, _store.Writes, "Accepting a document does not write records");

			var restarted = CreateService();
			_server.Json = Doc(Notice("a"), Notice("b", ",\"popup\":true"));
			Run(restarted.RefreshAsync());

			Assert.IsTrue(restarted.IsRead("b"));
			Assert.IsTrue(restarted.IsSnoozed("b", Now));
			Assert.AreEqual(1, restarted.GetUnreadCount(Now));
			Assert.IsEmpty(restarted.GetPopups(Now));
		}

		[Test]
		public void Popups_ExcludeSnoozedUntilNextLocalMidnight()
		{
			var service = Loaded(Notice("p1", ",\"popup\":true,\"priority\":1"), Notice("p2", ",\"popup\":true"), Notice("plain"));
			var changed = 0;
			service.Changed += () => changed++;

			Assert.AreEqual(new[] { "p1", "p2" }, Ids(service.GetPopups(Now)));

			// 03:00Z 는 test+9 에서 12:00. 다음 자정은 test+9 10-18 00:00 = 10-17 15:00Z
			service.SnoozeForToday("p1", Now);
			service.SnoozeForToday("p1", Now.AddHours(1));

			Assert.AreEqual(1, changed, "Same day, same snooze");
			Assert.AreEqual(new[] { "p2" }, Ids(service.GetPopups(Now)));
			Assert.IsTrue(service.IsSnoozed("p1", new DateTime(2026, 10, 17, 14, 59, 59, DateTimeKind.Utc)));
			Assert.IsFalse(service.IsSnoozed("p1", new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc)));
			Assert.AreEqual(new[] { "p1", "p2" }, Ids(service.GetPopups(new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc))));

			service.MarkRead("p2");
			Assert.AreEqual(new[] { "p2" }, Ids(service.GetPopups(Now)), "Reading does not remove popups");

			Assert.IsTrue(CreateService().IsSnoozed("p1", Now));
		}

		[Test]
		public void Snooze_PrunesExpiredRecordsWhenWriting()
		{
			var service = Loaded(Notice("p1", ",\"popup\":true"), Notice("p2", ",\"popup\":true"));
			service.SnoozeForToday("p1", Now);

			var nextDay = Now.AddDays(1);
			service.SnoozeForToday("p2", nextDay);

			Assert.IsFalse(_store.Value.Contains("p1"), _store.Value);
			Assert.IsTrue(_store.Value.Contains("p2"), _store.Value);
		}

		[Test]
		public void NextDayStart_SkipsMissingMidnight()
		{
			// 3월 마지막 일요일 00:00 에 시계가 01:00 으로 넘어가는 시간대(그날 자정이 없음)
			var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 5, DayOfWeek.Sunday);
			var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 5, DayOfWeek.Sunday);
			var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1), start, end);
			var zone = TimeZoneInfo.CreateCustomTimeZone("gap", TimeSpan.Zero, "gap", "gap", "gap-dst", new[] { rule });
			var options = Options();
			options.DayTimeZone = zone;
			var service = CreateService(options);

			// 2026-03-29 이 3월 마지막 일요일. 전날 22:00(UTC+0) 다음의 자정은 없고 01:00(UTC+1) = 00:00Z 가 그날의 시작
			Assert.AreEqual(new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc), service.NextDayStart(new DateTime(2026, 3, 28, 22, 0, 0, DateTimeKind.Utc)));
			// 여름 시간 중: 2026-07-01 12:00Z = 13:00 지역, 다음 자정 07-02 00:00 지역 = 07-01 23:00Z
			Assert.AreEqual(new DateTime(2026, 7, 1, 23, 0, 0, DateTimeKind.Utc), service.NextDayStart(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)));
		}

		[Test]
		public void NextDayStart_TakesTheEarlierOfTwoMidnights()
		{
			// 10월 마지막 일요일 01:00(여름 시간)에 00:00 으로 돌아가 그날 자정이 두 번 있는 시간대
			var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);
			var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 5, DayOfWeek.Sunday);
			var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1), start, end);
			var options = Options();
			options.DayTimeZone = TimeZoneInfo.CreateCustomTimeZone("twice", TimeSpan.Zero, "twice", "twice", "twice-dst", new[] { rule });
			var service = CreateService(options);

			// 2026-10-24T22:00Z 는 지역 23:00(UTC+1). 첫 자정 10-25 00:00(UTC+1) = 10-24T23:00Z
			Assert.AreEqual(new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc), service.NextDayStart(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc)));
		}

		[Test]
		public void NextDayStart_AfterTheClockWentBackToYesterday_TakesTheFutureMidnight()
		{
			// 10월 마지막 일요일 00:30(여름 시간)에 전날 23:30 으로 돌아가는 시간대: 10-25 00:00~00:30 이 23:00Z 와 00:00Z 두 번 온다
			var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);
			var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 30, 0), 10, 5, DayOfWeek.Sunday);
			var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1), start, end);
			var options = Options();
			options.DayTimeZone = TimeZoneInfo.CreateCustomTimeZone("back", TimeSpan.Zero, "back", "back", "back-dst", new[] { rule });
			_server.Json = Doc(Notice("p", ",\"popup\":true"));
			var service = CreateService(options);
			Run(service.RefreshAsync());

			var before = new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc);
			var after = new DateTime(2026, 10, 24, 23, 45, 0, DateTimeKind.Utc);
			var future = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

			// 돌아가기 전(지역 23:00 여름 시간)에는 처음 오는 자정 23:00Z
			Assert.AreEqual(new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc), service.NextDayStart(before));
			// 돌아간 뒤(지역 10-24 23:45 표준 시간)에는 23:00Z 가 지났으므로 00:00Z
			Assert.AreEqual(future, service.NextDayStart(after));

			service.SnoozeForToday("p", after);
			Assert.IsTrue(service.IsSnoozed("p", after));
			Assert.IsTrue(service.IsSnoozed("p", future.AddTicks(-1)));
			Assert.IsFalse(service.IsSnoozed("p", future));
		}

		[Test]
		public void NextDayStart_FindsTheTransitionOfAnOddSizedGap()
		{
			// 3월 마지막 일요일 00:00 에 45분 앞으로 가는 시간대(00:00~00:45 가 없음)
			var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 5, DayOfWeek.Sunday);
			var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 5, DayOfWeek.Sunday);
			var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromMinutes(45), start, end);
			var options = Options();
			options.DayTimeZone = TimeZoneInfo.CreateCustomTimeZone("odd", TimeSpan.Zero, "odd", "odd", "odd-dst", new[] { rule });
			var service = CreateService(options);

			// 첫 유효 시각 00:45(UTC+0:45) = 03-29T00:00Z
			Assert.AreEqual(new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc), service.NextDayStart(new DateTime(2026, 3, 28, 22, 0, 0, DateTimeKind.Utc)));
		}

		[Test]
		public void State_Unreadable_StartsEmptyWithWarning()
		{
			_store.Value = "{\"read\":[\"bad id\"]}";
			var service = CreateService();

			Assert.IsFalse(service.IsRead("bad id"));
			Assert.AreEqual(1, Count(LogLevel.Warning, "Ignored unreadable notice state"));

			_store.Value = "not json";
			CreateService();
			Assert.AreEqual(2, Count(LogLevel.Warning, "Ignored unreadable notice state"));

			_store.Value = "{\"read\":[\"a\"],\"snoozed\":{\"b\":\"2026-10-17\"}}";
			Assert.IsFalse(CreateService().IsRead("a"), "A broken entry drops the whole record");
		}

		[Test]
		public void State_WriteFailure_WarnsAndKeepsMemory()
		{
			var service = Loaded(Notice("a"));
			_store.FailWrites = true;

			service.MarkRead("a");

			Assert.IsTrue(service.IsRead("a"));
			Assert.AreEqual(1, Count(LogLevel.Warning, "Could not save notice state: quota exceeded"));
		}

		[Test]
		public void Changed_HandlerException_IsLoggedAndOthersRun()
		{
			var service = Loaded(Notice("a"));
			var second = 0;
			service.Changed += () => throw new InvalidOperationException("handler bug");
			service.Changed += () => second++;

			service.MarkRead("a");

			Assert.AreEqual(1, second);
			Assert.AreEqual(1, Count(LogLevel.Error, "handler bug"));
		}

		[Test]
		public void Misuse_Throws()
		{
			var service = Loaded(Notice("a"));
			var local = new DateTime(2026, 10, 17, 3, 0, 0, DateTimeKind.Local);

			Assert.Throws<ArgumentException>(() => service.GetNotices(local));
			Assert.Throws<ArgumentException>(() => service.GetUnreadCount(local));
			Assert.Throws<ArgumentException>(() => service.GetPopups(local));
			Assert.Throws<ArgumentException>(() => service.IsSnoozed("a", local));
			Assert.Throws<ArgumentException>(() => service.SnoozeForToday("a", local));
			Assert.Throws<ArgumentException>(() => service.MarkRead(null));
			Assert.Throws<ArgumentException>(() => service.MarkRead("a b"));
			Assert.Throws<ArgumentException>(() => service.SnoozeForToday("", Now));
		}
	}
}
