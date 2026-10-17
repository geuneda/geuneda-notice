using System;
using System.Security.Cryptography;

namespace Geuneda.Notice.Server
{
	/// <summary>내려줄 바이트와 ETag 입니다.</summary>
	internal sealed class NoticeSnapshot
	{
		public NoticeSnapshot(byte[] bytes, string etag)
		{
			Bytes = bytes;
			ETag = etag;
		}

		public byte[] Bytes { get; }

		/// <summary>바이트의 SHA-256(소문자 16진수)을 따옴표로 감싼 강한 ETag 입니다.</summary>
		public string ETag { get; }
	}

	/// <summary>
	/// 원천 문서에서 지금 보이는 공지만 남겨 직렬화합니다. 같은 문서 인스턴스이고 지금 시각이 직전 결과와 같은 기간 경계 구간 안이면 그 결과를 다시 씁니다.
	/// </summary>
	internal sealed class NoticePublisher
	{
		private readonly object _gate = new object();

		private NoticeDocument _source;
		private DateTime _validFrom;
		private DateTime _validUntil;
		private NoticeSnapshot _snapshot;

		public NoticeSnapshot Publish(NoticeDocument document, DateTime utcNow)
		{
			lock (_gate)
			{
				if (ReferenceEquals(document, _source) && utcNow >= _validFrom && utcNow < _validUntil)
				{
					return _snapshot;
				}

				var bytes = document.ActiveAt(utcNow).ToUtf8Json();
				_snapshot = new NoticeSnapshot(bytes, "\"" + Convert.ToHexStringLower(SHA256.HashData(bytes)) + "\"");
				_source = document;
				SetWindow(document, utcNow);
				return _snapshot;
			}
		}

		/// <summary>
		/// 보이는 공지 집합이 바뀌지 않는 구간 [가장 늦은 지난 경계, 가장 이른 다가올 경계)입니다. 공지는 시작 ≤ 지금 &lt; 끝일 때 보이므로 그 사이에서는 같습니다(시계가 뒤로 가도 맞음).
		/// </summary>
		private void SetWindow(NoticeDocument document, DateTime utcNow)
		{
			_validFrom = DateTime.MinValue;
			_validUntil = DateTime.MaxValue;

			foreach (var item in document.Items)
			{
				Narrow(item.StartsAt, utcNow);
				Narrow(item.EndsAt, utcNow);
			}
		}

		private void Narrow(DateTime? boundary, DateTime utcNow)
		{
			if (!boundary.HasValue)
			{
				return;
			}

			if (boundary.Value <= utcNow)
			{
				_validFrom = boundary.Value > _validFrom ? boundary.Value : _validFrom;
			}
			else
			{
				_validUntil = boundary.Value < _validUntil ? boundary.Value : _validUntil;
			}
		}
	}
}
