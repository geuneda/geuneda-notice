using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>기록을 둘 곳입니다. 테스트가 바꿔 끼웁니다.</summary>
	internal interface INoticeStateStore
	{
		/// <summary>저장한 글입니다. 없으면 null 입니다.</summary>
		string Read();

		/// <summary>저장합니다. 저장 한도(WebGL) 등으로 못 하면 false 와 이유입니다.</summary>
		bool TryWrite(string value, out string error);
	}

	/// <summary>
	/// PlayerPrefs 의 키 하나에 둡니다. 쓸 때마다 <c>PlayerPrefs.Save</c> 합니다(Unity 문서상 기본 저장 시점은 <c>OnApplicationQuit</c> 이라, 모바일에서 종료 처리 없이 끝나면 기록을 잃을 수 있음).
	/// 기록은 사용자 동작(읽음, 오늘 안 보기)과 문서 교체 때만 쓰므로 횟수가 적습니다.
	/// </summary>
	internal sealed class PlayerPrefsNoticeStateStore : INoticeStateStore
	{
		private readonly string _key;

		public PlayerPrefsNoticeStateStore(string key)
		{
			_key = key;
		}

		public string Read()
		{
			return PlayerPrefs.HasKey(_key) ? PlayerPrefs.GetString(_key) : null;
		}

		public bool TryWrite(string value, out string error)
		{
			try
			{
				PlayerPrefs.SetString(_key, value);
				PlayerPrefs.Save();
				error = null;
				return true;
			}
			catch (PlayerPrefsException exception)
			{
				error = exception.Message;
				return false;
			}
		}
	}
}
