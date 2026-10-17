using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Notice.Server
{
	/// <summary>
	/// 내려줄 공지 문서의 원천입니다. 게임 DB·운영 도구로 구현하거나 파일 원천(<see cref="NoticeServerBuilder.AddFileSource"/>)을 씁니다.
	/// </summary>
	/// <remarks>
	/// 요청마다 불립니다. 문서가 그대로면 <b>같은 인스턴스</b>를 돌려주세요. 배포기는 같은 인스턴스·같은 기간 경계 안이면 직렬화와 ETag 를 다시 계산하지 않습니다.
	/// 문서는 아직 시작하지 않은 공지까지 담아도 됩니다(엔드포인트가 지금 시각에 보이는 것만 내려줌). null 을 돌려주면 오류(500)입니다.
	/// </remarks>
	public interface INoticeSource
	{
		Task<NoticeDocument> GetDocumentAsync(CancellationToken cancellationToken);
	}
}
