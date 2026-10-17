using NUnit.Framework;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice.Tests
{
	[TestFixture]
	public class PlayerPrefsNoticeStateStoreTest
	{
		private const string Key = "geuneda.notice.tests.state";

		[SetUp]
		[TearDown]
		public void Clear()
		{
			PlayerPrefs.DeleteKey(Key);
		}

		[Test]
		public void ReadsWhatItWrote()
		{
			var store = new PlayerPrefsNoticeStateStore(Key);
			Assert.IsNull(store.Read());

			Assert.IsTrue(store.TryWrite("{\"read\":[\"a\"]}", out var error), error);
			Assert.AreEqual("{\"read\":[\"a\"]}", new PlayerPrefsNoticeStateStore(Key).Read());
		}

		[Test]
		public void Service_DefaultStore_UsesStateKey()
		{
			var server = new FakeNoticeServer { Json = "{\"notices\":[{\"id\":\"a\",\"contents\":{\"ko\":{\"title\":\"t\"}}}]}" };
			var options = new NoticeServiceOptions
			{
				HttpClient = server.CreateClient(),
				DocumentUri = FakeNoticeServer.DocumentPath,
				Language = "ko",
				StateKey = Key
			};

			new NoticeService(options).MarkRead("a");

			Assert.IsTrue(new NoticeService(options).IsRead("a"));
			StringAssert.Contains("\"a\"", PlayerPrefs.GetString(Key));
		}
	}
}
