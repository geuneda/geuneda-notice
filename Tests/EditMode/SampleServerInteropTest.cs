using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using Geuneda.Http;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice.Tests
{
	/// <summary>
	/// 실제 .NET 샘플 공지 서버(<c>Server~/samples/Geuneda.Notice.SampleServer</c>)와의 상호 운용을 확인합니다.
	/// 서버를 띄우고 <c>GEUNEDA_NOTICE_INTEROP_URL</c>(예: <c>http://127.0.0.1:5599</c>)을 설정한 뒤 이름으로 골라 실행합니다.
	/// </summary>
	[TestFixture]
	[Explicit("샘플 공지 서버가 필요합니다")]
	[Category("Interop")]
	public class SampleServerInteropTest
	{
		private const string UrlVariable = "GEUNEDA_NOTICE_INTEROP_URL";

		private static string ServerUrl()
		{
			var url = Environment.GetEnvironmentVariable(UrlVariable);

			if (string.IsNullOrEmpty(url))
			{
				Assert.Ignore($"{UrlVariable} is not set");
			}

			return url.TrimEnd('/') + "/";
		}

		[UnityTest]
		public IEnumerator SampleServer_ServesActiveNotices_AndAnswers304() => UniTask.ToCoroutine(async () =>
		{
			var server = new CountingTransport(new UnityWebRequestTransport());
			var http = new HttpClientBuilder().WithBaseAddress(ServerUrl()).WithTransport(server).Build();
			var service = new NoticeService(new NoticeServiceOptions
			{
				HttpClient = http,
				DocumentUri = "notices",
				Language = "ko-KR",
				Platform = NoticePlatforms.Android,
				StateKey = "geuneda.notice.tests.interop"
			});
			var changed = 0;
			service.Changed += () => changed++;

			var first = await service.RefreshAsync();
			Assert.IsTrue(first.IsSuccess, first.ToString());

			// 서버가 끝난 공지와 아직 시작하지 않은 공지를 뺀다
			Assert.AreEqual(new[] { "welcome", "maintenance-2026-10" }, service.Document.Items.Select(i => i.Id).ToArray());

			var now = DateTime.UtcNow;
			var notices = service.GetNotices(now);
			Assert.AreEqual(new[] { "welcome", "maintenance-2026-10" }, notices.Select(n => n.Id).ToArray());
			Assert.AreEqual("환영합니다", notices[0].Title);
			Assert.AreEqual("Mobile event", notices[1].Title, "Falls back to the default language");
			Assert.AreEqual(new[] { "maintenance-2026-10" }, service.GetPopups(now).Select(n => n.Id).ToArray());
			Assert.AreEqual(1, changed);

			var second = await service.RefreshAsync();
			Assert.IsTrue(second.IsSuccess, second.ToString());
			Assert.AreEqual(304, server.LastStatus);
			Assert.AreEqual(1, changed);

			UnityEngine.PlayerPrefs.DeleteKey("geuneda.notice.tests.interop");
		});

		private sealed class CountingTransport : IHttpTransport
		{
			private readonly IHttpTransport _inner;

			public CountingTransport(IHttpTransport inner)
			{
				_inner = inner;
			}

			public int LastStatus { get; private set; }

			public async UniTask<Geuneda.Core.Result<HttpResponse>> SendAsync(HttpRequest request, System.Threading.CancellationToken cancellationToken)
			{
				var result = await _inner.SendAsync(request, cancellationToken);
				LastStatus = result.IsSuccess ? result.Value.StatusCode : 0;
				return result;
			}
		}
	}
}
