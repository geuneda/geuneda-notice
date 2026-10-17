using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Geuneda.Notice.Server
{
	/// <summary>
	/// 공지 문서 파일을 엄격하게 읽어(모르는 속성 거절) 메모리에 두고 내려줍니다. 파일은 1초에 한 번까지 다시 읽어 내용이 바뀌었으면 검증 후 바꿉니다.
	/// </summary>
	/// <remarks>
	/// 바뀐 파일이 검증에 실패하면 지금 문서를 계속 내려주고 오류 로그를 한 번 남깁니다. 파일 크기·시각이 아니라 내용으로 비교합니다.
	/// 운영 중 교체는 임시 파일에 쓴 뒤 이름을 바꿔(원자적 교체) 반쯤 쓴 파일을 읽지 않게 합니다.
	/// </remarks>
	internal sealed class NoticeFileSource : INoticeSource
	{
		internal static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

		private readonly string _path;
		private readonly TimeProvider _time;
		private readonly ILogger<NoticeFileSource> _logger;
		private readonly object _gate = new object();

		private NoticeDocument _current;
		private byte[] _currentBytes;
		private byte[] _rejected;
		private bool _readFailed;
		private DateTimeOffset _nextCheck;

		public NoticeFileSource(string path, IHostEnvironment environment, TimeProvider time, ILogger<NoticeFileSource> logger)
		{
			_path = Path.GetFullPath(path, environment.ContentRootPath);
			_time = time;
			_logger = logger;
		}

		/// <summary>처음 읽기입니다. 읽거나 검증하지 못하면 시작을 막습니다(<c>MapGeunedaNotice</c> 가 부름).</summary>
		/// <exception cref="InvalidOperationException">파일이 없거나 문서가 맞지 않는 경우</exception>
		public void Load()
		{
			lock (_gate)
			{
				if (_current != null)
				{
					return;
				}

				byte[] bytes;

				try
				{
					bytes = File.ReadAllBytes(_path);
				}
				catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
				{
					throw new InvalidOperationException($"Cannot read notice document '{_path}': {exception.Message}", exception);
				}

				try
				{
					_current = NoticeDocument.Parse(bytes, true);
				}
				catch (NoticeDocumentException exception)
				{
					throw new InvalidOperationException($"Notice document '{_path}' is invalid: {exception.Message}", exception);
				}

				_currentBytes = bytes;
				_nextCheck = _time.GetUtcNow() + CheckInterval;
				_logger.LogInformation("Serving {Count} notices from {Path}", _current.Items.Count, _path);
			}
		}

		public Task<NoticeDocument> GetDocumentAsync(CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				if (_current == null)
				{
					throw new InvalidOperationException("Call Load() (MapGeunedaNotice) before serving");
				}

				var now = _time.GetUtcNow();

				if (now >= _nextCheck)
				{
					_nextCheck = now + CheckInterval;
					CheckFile();
				}

				return Task.FromResult(_current);
			}
		}

		private void CheckFile()
		{
			byte[] bytes;

			try
			{
				bytes = File.ReadAllBytes(_path);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				if (!_readFailed)
				{
					_readFailed = true;
					_logger.LogError(exception, "Cannot read notice document {Path}; still serving the last one", _path);
				}

				return;
			}

			_readFailed = false;

			if (bytes.AsSpan().SequenceEqual(_currentBytes) || (_rejected != null && bytes.AsSpan().SequenceEqual(_rejected)))
			{
				return;
			}

			NoticeDocument next;

			try
			{
				next = NoticeDocument.Parse(bytes, true);
			}
			catch (NoticeDocumentException exception)
			{
				_rejected = bytes;
				_logger.LogError("Rejected notice document {Path} ({Message}); still serving the last one", _path, exception.Message);
				return;
			}

			_current = next;
			_currentBytes = bytes;
			_rejected = null;
			_logger.LogInformation("Serving {Count} notices from {Path}", next.Items.Count, _path);
		}
	}
}
