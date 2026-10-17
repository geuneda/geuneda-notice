using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Geuneda.Notice.Server.Tests
{
	/// <summary>127.0.0.1 빈 포트에 띄운 공지 서버입니다. 콘텐츠 루트는 임시 폴더이고 시간은 <see cref="Time"/> 으로 움직입니다.</summary>
	internal sealed class NoticeTestServer : IAsyncDisposable
	{
		public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

		private readonly WebApplication _app;

		private NoticeTestServer(WebApplication app, string folder, FakeTimeProvider time, ListLoggerProvider logs, Uri address)
		{
			_app = app;
			Folder = folder;
			Time = time;
			Logs = logs;
			Http = new HttpClient { BaseAddress = address };
		}

		public string Folder { get; }
		public FakeTimeProvider Time { get; }
		public ListLoggerProvider Logs { get; }
		public HttpClient Http { get; }
		public IServiceProvider Services => _app.Services;

		public string DocumentPath => Path.Combine(Folder, "notices.json");

		/// <summary>콘텐츠 루트의 <c>notices.json</c> 을 상대 경로로 내려주는 서버를 띄웁니다.</summary>
		public static Task<NoticeTestServer> StartAsync(string document)
		{
			var folder = NewFolder();
			File.WriteAllText(Path.Combine(folder, "notices.json"), document, new UTF8Encoding(false));
			return StartAsync(folder, notice => notice.AddFileSource("notices.json"));
		}

		/// <summary>원천을 직접 붙이는 서버를 띄웁니다.</summary>
		public static async Task<NoticeTestServer> StartAsync(string folder, Action<NoticeServerBuilder> configure,
			Action<NoticeServerOptions> options = null)
		{
			var time = new FakeTimeProvider(Start);
			var logs = new ListLoggerProvider();
			var app = Build(folder, time, logs, configure, options);
			app.MapGeunedaNotice();
			await app.StartAsync();

			var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
			return new NoticeTestServer(app, folder, time, logs, new Uri(address + "/"));
		}

		public static WebApplication Build(string folder, TimeProvider time, ILoggerProvider logs, Action<NoticeServerBuilder> configure,
			Action<NoticeServerOptions> options = null)
		{
			var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = folder });
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Logging.ClearProviders();
			builder.Logging.AddProvider(logs);
			builder.Services.AddSingleton(time);
			configure(builder.Services.AddGeunedaNotice(options));
			return builder.Build();
		}

		public static string NewFolder()
		{
			var folder = Path.Combine(Path.GetTempPath(), "geuneda-notice-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(folder);
			return folder;
		}

		/// <summary>임시 파일에 쓴 뒤 이름을 바꿔 문서를 교체합니다.</summary>
		public void Replace(string document)
		{
			var temporary = DocumentPath + ".tmp";
			File.WriteAllText(temporary, document, new UTF8Encoding(false));
			File.Move(temporary, DocumentPath, true);
		}

		public async ValueTask DisposeAsync()
		{
			Http.Dispose();
			await _app.StopAsync();
			await _app.DisposeAsync();
			Directory.Delete(Folder, true);
		}
	}

	/// <summary>로그를 모읍니다.</summary>
	internal sealed class ListLoggerProvider : ILoggerProvider
	{
		public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new ConcurrentQueue<(LogLevel, string)>();

		public ILogger CreateLogger(string categoryName)
		{
			return new ListLogger(this);
		}

		public void Dispose()
		{
		}

		public int Count(LogLevel level, string contains)
		{
			return Entries.Count(entry => entry.Level == level && entry.Message.Contains(contains, StringComparison.Ordinal));
		}

		private sealed class ListLogger : ILogger
		{
			private readonly ListLoggerProvider _provider;

			public ListLogger(ListLoggerProvider provider)
			{
				_provider = provider;
			}

			public IDisposable BeginScope<TState>(TState state)
			{
				return null;
			}

			public bool IsEnabled(LogLevel logLevel)
			{
				return true;
			}

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
			{
				_provider.Entries.Enqueue((logLevel, formatter(state, exception)));
			}
		}
	}
}
