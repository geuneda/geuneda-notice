using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>
	/// 기기에 남기는 읽음·'오늘 안 보기' 기록입니다. 형식: <c>{"read":["id",...],"snoozed":{"id":"2026-10-18T15:00:00Z",...}}</c>(읽음은 읽은 순서).
	/// </summary>
	/// <remarks>
	/// 받은 문서에 없다는 이유로는 기록을 지우지 않습니다(오래된 CDN 사본이나 잠시 내린 공지 한 번에 기록을 잃지 않게). 읽음은 최근 <see cref="MaxReadRecords"/> 개만 남깁니다.
	/// </remarks>
	internal sealed class NoticeState
	{
		/// <summary>남길 읽음 기록 수입니다. 넘으면 가장 먼저 읽은 것부터 지웁니다.</summary>
		public const int MaxReadRecords = 500;

		private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

		private readonly List<string> _readOrder = new List<string>();
		private readonly HashSet<string> _read = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>공지 ID → 다시 띄울 시각(UTC)입니다.</summary>
		public Dictionary<string, DateTime> Snoozed { get; } = new Dictionary<string, DateTime>(StringComparer.Ordinal);

		public bool IsRead(string id)
		{
			return id != null && _read.Contains(id);
		}

		/// <summary>읽음을 더합니다. 처음이면 true 입니다. 기록이 <see cref="MaxReadRecords"/> 개를 넘으면 가장 먼저 읽은 것을 지웁니다.</summary>
		public bool AddRead(string id)
		{
			if (!_read.Add(id))
			{
				return false;
			}

			_readOrder.Add(id);

			while (_readOrder.Count > MaxReadRecords)
			{
				_read.Remove(_readOrder[0]);
				_readOrder.RemoveAt(0);
			}

			return true;
		}

		/// <summary>저장한 글을 읽습니다. 읽지 못하면 빈 기록과 이유입니다(기록 손상이 공지 표시를 막지 않게).</summary>
		public static NoticeState Parse(string text, out string error)
		{
			var state = new NoticeState();
			error = null;

			if (text == null)
			{
				return state;
			}

			try
			{
				JObject root;

				using (var reader = new JsonTextReader(new StringReader(text)))
				{
					reader.DateParseHandling = DateParseHandling.None;
					root = JToken.ReadFrom(reader) as JObject;
				}

				if (root == null)
				{
					error = "not a JSON object";
					return new NoticeState();
				}

				if (root["read"] is JArray read)
				{
					foreach (var id in read)
					{
						if (id.Type != JTokenType.String || !NoticeNames.IsId((string) id))
						{
							error = $"invalid read id {id}";
							return new NoticeState();
						}

						state.AddRead((string) id);
					}
				}

				if (root["snoozed"] is JObject snoozed)
				{
					foreach (var property in snoozed.Properties())
					{
						if (!NoticeNames.IsId(property.Name) || property.Value.Type != JTokenType.String ||
							!DateTime.TryParseExact((string) property.Value, UtcFormat, CultureInfo.InvariantCulture,
								DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var until))
						{
							error = $"invalid snooze entry '{property.Name}'";
							return new NoticeState();
						}

						state.Snoozed[property.Name] = until;
					}
				}
			}
			catch (JsonException exception)
			{
				error = exception.Message;
				return new NoticeState();
			}

			return state;
		}

		public string ToJson()
		{
			var snoozed = new JObject();

			foreach (var pair in Snoozed)
			{
				snoozed[pair.Key] = pair.Value.ToString(UtcFormat, CultureInfo.InvariantCulture);
			}

			return new JObject { ["read"] = new JArray(_readOrder.ToArray()), ["snoozed"] = snoozed }.ToString(Formatting.None);
		}
	}
}
