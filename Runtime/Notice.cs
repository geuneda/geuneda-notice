using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Notice
{
	/// <summary>
	/// 보여 줄 공지 하나입니다. 문서의 공지(<see cref="Item"/>)와 지금 언어에 맞춘 내용(<see cref="Content"/>)을 묶습니다. 만든 뒤 바뀌지 않습니다.
	/// </summary>
	public sealed class Notice
	{
		internal Notice(NoticeItem item, NoticeContent content)
		{
			Item = item;
			Content = content;
		}

		public NoticeItem Item { get; }

		public NoticeContent Content { get; }

		public string Id => Item.Id;

		public string Category => Item.Category;

		public int Priority => Item.Priority;

		public DateTime? StartsAt => Item.StartsAt;

		public DateTime? EndsAt => Item.EndsAt;

		public bool Popup => Item.Popup;

		public string Action => Item.Action;

		public string Title => Content.Title;

		public string Body => Content.Body;

		public string LinkUrl => Content.LinkUrl;

		public string LinkLabel => Content.LinkLabel;

		public override string ToString()
		{
			return $"Notice {Id} ({Category})";
		}
	}
}
