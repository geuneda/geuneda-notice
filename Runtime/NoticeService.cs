using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>
	/// 기본 공지 목록입니다. 부트스트랩에서 만들어 <see cref="INoticeService"/> 로 바인딩합니다. 동작 규칙은 <see cref="INoticeService"/> 를 봅니다.
	/// </summary>
	public sealed class NoticeService : INoticeService
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Notice");

		private readonly IHttpClient _http;
		private readonly HttpRequest _request;
		private readonly string _platform;
		private readonly TimeZoneInfo _dayZone;
		private readonly INoticeStateStore _store;
		private readonly NoticeState _state;

		private string _language;
		private NoticeDocument _document;
		private byte[] _documentBytes;
		private string _etag;
		private UniTaskCompletionSource<Result> _refresh;

		public event Action Changed;

		/// <summary>메인 스레드에서 만듭니다(PlayerPrefs 에서 기록을 읽음). 요청은 <see cref="RefreshAsync"/> 에서 보냅니다.</summary>
		/// <exception cref="ArgumentNullException"><paramref name="options"/> 가 null 인 경우</exception>
		/// <exception cref="ArgumentException">필수 설정이 없거나, 문서 주소가 주소 형식이 아니거나, 언어 태그·플랫폼 이름이 규칙에 맞지 않거나, 제한 시간이 0 이하인 경우</exception>
		public NoticeService(NoticeServiceOptions options) : this(options, null)
		{
		}

		internal NoticeService(NoticeServiceOptions options, INoticeStateStore store)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			if (options.HttpClient == null || string.IsNullOrEmpty(options.DocumentUri) || string.IsNullOrEmpty(options.StateKey))
			{
				throw new ArgumentException("HttpClient, DocumentUri and StateKey are required", nameof(options));
			}

			if (options.RequestTimeout <= TimeSpan.Zero)
			{
				throw new ArgumentException("RequestTimeout must be positive", nameof(options));
			}

			_language = NoticeNames.NormalizeLanguage(options.Language) ??
				throw new ArgumentException($"Language must be a language tag such as 'ko' or 'zh-Hant': '{options.Language}'", nameof(options));
			_platform = options.Platform ?? NoticePlatforms.Current;

			if (!NoticeNames.IsPlatform(_platform))
			{
				throw new ArgumentException($"Platform must be 1-{NoticeNames.MaxPlatformLength} lowercase letters, digits, '_' or '-': '{_platform}'",
					nameof(options));
			}

			_http = options.HttpClient;
			_request = HttpRequest.Get(options.DocumentUri).WithTimeout(options.RequestTimeout);
			_dayZone = options.DayTimeZone ?? TimeZoneInfo.Local;
			_store = store ?? new PlayerPrefsNoticeStateStore(options.StateKey);
			_state = NoticeState.Parse(_store.Read(), out var error);

			if (error != null)
			{
				Logger.Warning($"Ignored unreadable notice state '{options.StateKey}' ({error}); read and snooze records start empty");
			}
		}

		public NoticeDocument Document => _document;

		/// <summary>비교용 모양(소문자, <c>-</c> 구분)의 언어 태그입니다.</summary>
		public string Language
		{
			get => _language;
			set
			{
				var language = NoticeNames.NormalizeLanguage(value) ??
					throw new ArgumentException($"Language must be a language tag such as 'ko' or 'zh-Hant': '{value}'", nameof(value));

				if (language == _language)
				{
					return;
				}

				_language = language;
				RaiseChanged();
			}
		}

		public async UniTask<Result> RefreshAsync(CancellationToken cancellationToken = default)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Result.Fail(ResultCodes.Cancelled, "Notice refresh was cancelled");
			}

			// 진행 중인 요청 하나를 여러 호출이 나눠 받는다(UniTask.Preserve 는 완료 전 다중 대기를 받지 않음)
			var refresh = _refresh;

			if (refresh == null)
			{
				refresh = new UniTaskCompletionSource<Result>();
				_refresh = refresh;
				RunRefreshAsync(refresh).Forget();
			}

			try
			{
				return await refresh.Task.AttachExternalCancellation(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// 이 호출만 기다리기를 멈춘다. 요청은 제한 시간까지 계속되고 결과는 적용된다
				return Result.Fail(ResultCodes.Cancelled, "Notice refresh was cancelled");
			}
		}

		public IReadOnlyList<Notice> GetNotices(DateTime utcNow, string category = null)
		{
			return Collect(utcNow, category, false);
		}

		public int GetUnreadCount(DateTime utcNow, string category = null)
		{
			var count = 0;

			foreach (var notice in Collect(utcNow, category, false))
			{
				if (!_state.IsRead(notice.Id))
				{
					count++;
				}
			}

			return count;
		}

		public IReadOnlyList<Notice> GetPopups(DateTime utcNow)
		{
			return Collect(utcNow, null, true);
		}

		public bool IsRead(string id)
		{
			return _state.IsRead(id);
		}

		public void MarkRead(string id)
		{
			RequireId(id);

			if (!_state.AddRead(id))
			{
				return;
			}

			Save();
			RaiseChanged();
		}

		public bool IsSnoozed(string id, DateTime utcNow)
		{
			RequireUtc(utcNow);
			return id != null && _state.Snoozed.TryGetValue(id, out var until) && utcNow < until;
		}

		public void SnoozeForToday(string id, DateTime utcNow)
		{
			RequireId(id);
			RequireUtc(utcNow);

			var until = NextDayStart(utcNow);

			if (_state.Snoozed.TryGetValue(id, out var current) && current == until)
			{
				return;
			}

			// 지난 기록은 쓸 때 함께 지운다(시각을 아는 때가 이때뿐)
			var expired = new List<string>();

			foreach (var pair in _state.Snoozed)
			{
				if (pair.Value <= utcNow)
				{
					expired.Add(pair.Key);
				}
			}

			foreach (var key in expired)
			{
				_state.Snoozed.Remove(key);
			}

			_state.Snoozed[id] = until;
			Save();
			RaiseChanged();
		}

		/// <summary>
		/// 하루 기준 시간대에서 <paramref name="utcNow"/> 다음 날이 시작하는 UTC 시각입니다. 자정이 두 번 있는 날(일광 절약 시간 끝)은 지금 이후 먼저 오는 자정,
		/// 자정이 없는 날(일광 절약 시간 시작)은 시계가 넘어간 순간(그날 처음 있는 시각)입니다.
		/// </summary>
		internal DateTime NextDayStart(DateTime utcNow)
		{
			var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, _dayZone);
			var midnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);

			if (_dayZone.IsAmbiguousTime(midnight))
			{
				// 오프셋마다 UTC 후보(지역 - 오프셋)가 있다. 시계가 날짜를 넘어 전날로 돌아간 뒤라면 이른 후보는 이미 지났으므로 지금 이후 중 가장 이른 것을 고른다
				var earliest = DateTime.MaxValue;

				foreach (var offset in _dayZone.GetAmbiguousTimeOffsets(midnight))
				{
					var candidate = DateTime.SpecifyKind(midnight - offset, DateTimeKind.Utc);

					if (candidate > utcNow && candidate < earliest)
					{
						earliest = candidate;
					}
				}

				if (earliest != DateTime.MaxValue)
				{
					return earliest;
				}
			}
			else if (!_dayZone.IsInvalidTime(midnight))
			{
				return TimeZoneInfo.ConvertTimeToUtc(midnight, _dayZone);
			}

			// 없는 자정: 지역 시각이 처음 자정 이상이 되는 UTC 순간(시계가 넘어간 순간)을 이분 탐색으로 찾는다.
			// 지금은 자정 전이고 하루 안에 시계가 두 번 바뀌지 않으므로 그 사이 지역 시각은 늘기만 한다
			var low = utcNow.Ticks;
			var high = utcNow.AddDays(2).Ticks;

			while (low < high)
			{
				var middle = low + (high - low) / 2;

				if (TimeZoneInfo.ConvertTimeFromUtc(new DateTime(middle, DateTimeKind.Utc), _dayZone) >= midnight)
				{
					high = middle;
				}
				else
				{
					low = middle + 1;
				}
			}

			return new DateTime(low, DateTimeKind.Utc);
		}

		private List<Notice> Collect(DateTime utcNow, string category, bool popups)
		{
			RequireUtc(utcNow);

			var visible = new List<KeyValuePair<int, Notice>>();
			var document = _document;

			if (document == null)
			{
				return new List<Notice>();
			}

			for (var i = 0; i < document.Items.Count; i++)
			{
				var item = document.Items[i];

				if ((category != null && !string.Equals(item.Category, category, StringComparison.Ordinal)) || !item.IsActiveAt(utcNow) ||
					!item.IsFor(_platform))
				{
					continue;
				}

				if (popups && (!item.Popup || IsSnoozed(item.Id, utcNow)))
				{
					continue;
				}

				var content = item.FindContent(_language, document.DefaultLanguage);

				if (content != null)
				{
					visible.Add(new KeyValuePair<int, Notice>(i, new Notice(item, content)));
				}
			}

			visible.Sort(CompareNotices);
			var result = new List<Notice>(visible.Count);

			foreach (var pair in visible)
			{
				result.Add(pair.Value);
			}

			return result;
		}

		private static int CompareNotices(KeyValuePair<int, Notice> x, KeyValuePair<int, Notice> y)
		{
			var byPriority = y.Value.Priority.CompareTo(x.Value.Priority);

			if (byPriority != 0)
			{
				return byPriority;
			}

			var byStart = (y.Value.StartsAt ?? DateTime.MinValue).CompareTo(x.Value.StartsAt ?? DateTime.MinValue);
			return byStart != 0 ? byStart : x.Key.CompareTo(y.Key);
		}

		private async UniTaskVoid RunRefreshAsync(UniTaskCompletionSource<Result> refresh)
		{
			Result result;

			try
			{
				result = await FetchAsync();
			}
			catch (Exception exception)
			{
				ReleaseRefresh(refresh);
				refresh.TrySetException(exception);
				return;
			}

			ReleaseRefresh(refresh);
			refresh.TrySetResult(result);
		}

		private void ReleaseRefresh(UniTaskCompletionSource<Result> refresh)
		{
			if (ReferenceEquals(_refresh, refresh))
			{
				_refresh = null;
			}
		}

		private async UniTask<Result> FetchAsync()
		{
			var conditional = _document != null && _etag != null;
			var request = conditional ? _request.WithHeader("If-None-Match", _etag) : _request;

			// 함께 기다리는 호출이 있어 호출자 토큰을 넘기지 않는다. 요청 제한 시간이 끝을 보장한다
			var sent = await _http.SendAsync(request);

			if (!sent.IsSuccess)
			{
				return Result.Fail(sent.Code, sent.Message);
			}

			var response = sent.Value;

			if (response.StatusCode == 304)
			{
				return conditional ? Result.Ok() : Result.Fail(HttpResultCodes.HttpError, $"304 without a conditional request from {response.Uri}");
			}

			if (!response.IsSuccessStatusCode)
			{
				return Result.Fail(HttpResultCodes.HttpError, $"Unexpected status {response.StatusCode} from {response.Uri}");
			}

			if (_documentBytes != null && response.Body.AsSpan().SequenceEqual(_documentBytes))
			{
				_etag = ReadETag(response);
				return Result.Ok();
			}

			NoticeDocument document;

			try
			{
				document = NoticeDocument.Parse(response.Body);
			}
			catch (NoticeDocumentException exception)
			{
				Logger.Warning($"Rejected notice document from {response.Uri}: {exception.Message}");
				return Result.Fail(NoticeResultCodes.InvalidDocument, exception.Message);
			}

			_document = document;
			_documentBytes = response.Body;
			_etag = ReadETag(response);
			Logger.Info($"Applied notice document with {document.Items.Count} notices from {response.Uri}");
			RaiseChanged();
			return Result.Ok();
		}

		private void Save()
		{
			if (!_store.TryWrite(_state.ToJson(), out var error))
			{
				// 이번 실행의 기록은 메모리에 남는다. 다음 실행에서는 마지막으로 저장한 기록부터 시작한다
				Logger.Warning($"Could not save notice state: {error}");
			}
		}

		private void RaiseChanged()
		{
			foreach (var handler in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
			{
				try
				{
					((Action) handler)();
				}
				catch (Exception exception)
				{
					Logger.Error(exception);
				}
			}
		}

		private static void RequireUtc(DateTime utcNow)
		{
			if (utcNow.Kind != DateTimeKind.Utc)
			{
				throw new ArgumentException("Time must be UTC", nameof(utcNow));
			}
		}

		private static void RequireId(string id)
		{
			if (!NoticeNames.IsId(id))
			{
				throw new ArgumentException($"Notice id must be 1-{NoticeNames.MaxIdLength} letters, digits, '.', '_' or '-': '{id}'", nameof(id));
			}
		}

		private static string ReadETag(HttpResponse response)
		{
			return response.Headers.TryGetValue("ETag", out var etag) && !string.IsNullOrEmpty(etag) ? etag : null;
		}
	}
}
