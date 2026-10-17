using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Geuneda.Notice.Server.Tests
{
	public class EndpointTest
	{
		private const string Document = @"{
  ""defaultLanguage"": ""en"",
  ""notices"": [
    { ""id"": ""ended"", ""endsAt"": ""2026-10-17T00:00:00Z"", ""contents"": { ""en"": { ""title"": ""ended"" } } },
    { ""id"": ""always"", ""contents"": { ""en"": { ""title"": ""always"" } } },
    { ""id"": ""soon"", ""startsAt"": ""2026-10-17T01:00:00Z"", ""endsAt"": ""2026-10-17T02:00:00Z"", ""contents"": { ""en"": { ""title"": ""soon"" } } }
  ]
}";

		private static async Task<HttpResponseMessage> GetAsync(NoticeTestServer server, string ifNoneMatch = null)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, "notices");

			if (ifNoneMatch != null)
			{
				request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
			}

			return await server.Http.SendAsync(request);
		}

		private static async Task<string[]> ServedIdsAsync(NoticeTestServer server)
		{
			var response = await GetAsync(server);
			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			return NoticeDocument.Parse(await response.Content.ReadAsByteArrayAsync(), true).Items.Select(item => item.Id).ToArray();
		}

		[Fact]
		public async Task Get_ServesOnlyActiveNotices_WithETagAndCacheControl()
		{
			await using var server = await NoticeTestServer.StartAsync(Document);

			var response = await GetAsync(server);
			var bytes = await response.Content.ReadAsByteArrayAsync();
			var document = NoticeDocument.Parse(bytes, true);

			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.Equal(new[] { "always" }, document.Items.Select(item => item.Id));
			Assert.Equal("en", document.DefaultLanguage);
			Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
			Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
			Assert.False(response.Headers.ETag.IsWeak);
			Assert.Equal(66, response.Headers.ETag.Tag.Length);
			Assert.True(response.Headers.CacheControl.NoCache);
		}

		[Fact]
		public async Task Get_MatchingIfNoneMatch_Is304WithETag()
		{
			await using var server = await NoticeTestServer.StartAsync(Document);
			var etag = (await GetAsync(server)).Headers.ETag.Tag;

			foreach (var header in new[] { etag, "W/" + etag, "\"other\", " + etag, "*" })
			{
				var response = await GetAsync(server, header);

				Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
				Assert.Equal(etag, response.Headers.ETag.Tag);
				Assert.Empty(await response.Content.ReadAsByteArrayAsync());
			}

			Assert.Equal(HttpStatusCode.OK, (await GetAsync(server, "\"other\"")).StatusCode);
			Assert.Equal(HttpStatusCode.OK, (await GetAsync(server, "not an etag")).StatusCode);
		}

		[Fact]
		public async Task Get_FollowsPeriodBoundaries()
		{
			await using var server = await NoticeTestServer.StartAsync(Document);
			var first = (await GetAsync(server)).Headers.ETag.Tag;

			server.Time.Advance(TimeSpan.FromMinutes(59));
			Assert.Equal(first, (await GetAsync(server)).Headers.ETag.Tag);

			server.Time.Advance(TimeSpan.FromMinutes(1));
			Assert.Equal(new[] { "always", "soon" }, await ServedIdsAsync(server));
			Assert.Equal(HttpStatusCode.OK, (await GetAsync(server, first)).StatusCode);

			server.Time.Advance(TimeSpan.FromHours(1));
			Assert.Equal(new[] { "always" }, await ServedIdsAsync(server));
			Assert.Equal(first, (await GetAsync(server)).Headers.ETag.Tag);
		}

		[Fact]
		public async Task FileSource_ReplacesValidFile_KeepsLastOnInvalidFile()
		{
			await using var server = await NoticeTestServer.StartAsync(Document);

			server.Replace("{\"notices\":[{\"id\":\"next\",\"contents\":{\"en\":{\"title\":\"next\"}}}]}");
			Assert.Equal(new[] { "always" }, await ServedIdsAsync(server));

			server.Time.Advance(NoticeFileSource.CheckInterval);
			Assert.Equal(new[] { "next" }, await ServedIdsAsync(server));

			server.Replace("{\"notices\":[{\"id\":\"typo\",\"startAt\":\"2026-10-17T00:00:00Z\",\"contents\":{\"en\":{\"title\":\"t\"}}}]}");

			for (var i = 0; i < 3; i++)
			{
				server.Time.Advance(NoticeFileSource.CheckInterval);
				Assert.Equal(new[] { "next" }, await ServedIdsAsync(server));
			}

			Assert.Equal(1, server.Logs.Count(LogLevel.Error, "unknown property 'startAt'"));

			File.Delete(server.DocumentPath);
			server.Time.Advance(NoticeFileSource.CheckInterval);
			Assert.Equal(new[] { "next" }, await ServedIdsAsync(server));
			server.Time.Advance(NoticeFileSource.CheckInterval);
			Assert.Equal(new[] { "next" }, await ServedIdsAsync(server));
			Assert.Equal(1, server.Logs.Count(LogLevel.Error, "Cannot read notice document"));
		}

		[Fact]
		public void Map_InvalidOrMissingFile_FailsAtStartup()
		{
			var folder = NoticeTestServer.NewFolder();

			try
			{
				File.WriteAllText(Path.Combine(folder, "notices.json"), "{\"notices\":[],\"extra\":1}");
				var invalid = NoticeTestServer.Build(folder, TimeProvider.System, new ListLoggerProvider(), notice => notice.AddFileSource("notices.json"));
				var error = Assert.Throws<InvalidOperationException>(() => invalid.MapGeunedaNotice());
				Assert.Contains("unknown property 'extra'", error.Message);

				var missing = NoticeTestServer.Build(folder, TimeProvider.System, new ListLoggerProvider(), notice => notice.AddFileSource("missing.json"));
				Assert.Contains("Cannot read notice document", Assert.Throws<InvalidOperationException>(() => missing.MapGeunedaNotice()).Message);
			}
			finally
			{
				Directory.Delete(folder, true);
			}
		}

		[Fact]
		public void Map_WithoutRegistration_Throws()
		{
			var builder = WebApplication.CreateSlimBuilder();
			var app = builder.Build();
			Assert.Contains("AddGeunedaNotice", Assert.Throws<InvalidOperationException>(() => app.MapGeunedaNotice()).Message);

			var noSource = WebApplication.CreateSlimBuilder();
			noSource.Services.AddGeunedaNotice();
			Assert.Contains("notice source", Assert.Throws<InvalidOperationException>(() => noSource.Build().MapGeunedaNotice()).Message);

			Assert.Throws<ArgumentException>(() => new ServiceCollection().AddGeunedaNotice().AddFileSource(" "));
		}

		[Fact]
		public async Task CustomSource_ReplacesFileSource_AndIsNotReadAtStartup()
		{
			var folder = NoticeTestServer.NewFolder();
			await using var server = await NoticeTestServer.StartAsync(folder, notice => notice.AddFileSource("missing.json").AddSource<ScriptedSource>());
			var source = (ScriptedSource) server.Services.GetRequiredService<INoticeSource>();

			Assert.Equal(0, source.Calls);
			source.Document = NoticeDocument.Parse(Document);
			Assert.Equal(new[] { "always" }, await ServedIdsAsync(server));
			Assert.Equal(1, source.Calls);

			source.Document = null;
			Assert.Equal(HttpStatusCode.InternalServerError, (await GetAsync(server)).StatusCode);
		}

		[Fact]
		public async Task Options_SetCacheControl()
		{
			var folder = NoticeTestServer.NewFolder();
			await using var server = await NoticeTestServer.StartAsync(folder, notice => notice.AddSource<ScriptedSource>(),
				options => options.CacheControl = "public, max-age=60");
			((ScriptedSource) server.Services.GetRequiredService<INoticeSource>()).Document = NoticeDocument.Parse(Document);

			Assert.Equal(TimeSpan.FromSeconds(60), (await GetAsync(server)).Headers.CacheControl.MaxAge);
		}

		[Fact]
		public void Publisher_ReusesSnapshotInsideWindow_AndRecomputesOutside()
		{
			var publisher = new NoticePublisher();
			var document = NoticeDocument.Parse(Document);
			var now = NoticeTestServer.Start.UtcDateTime;

			var first = publisher.Publish(document, now);
			Assert.Same(first, publisher.Publish(document, now.AddMinutes(59)));

			var later = publisher.Publish(document, now.AddHours(1));
			Assert.NotEqual(first.ETag, later.ETag);

			// 시계가 경계 앞으로 돌아가도 다시 계산한다
			Assert.Equal(first.ETag, publisher.Publish(document, now).ETag);

			// 같은 내용이라도 다른 인스턴스면 다시 계산한다(결과는 같음)
			var copy = NoticeDocument.Parse(Document);
			var recomputed = publisher.Publish(copy, now);
			Assert.NotSame(first, recomputed);
			Assert.Equal(first.ETag, recomputed.ETag);
		}

		private sealed class ScriptedSource : INoticeSource
		{
			public NoticeDocument Document;
			public int Calls;

			public Task<NoticeDocument> GetDocumentAsync(CancellationToken cancellationToken)
			{
				Calls++;
				return Task.FromResult(Document);
			}
		}
	}
}
