using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Geuneda.Notice.Server
{
	/// <summary><see cref="NoticeServerExtensions.AddGeunedaNotice"/> 가 돌려주는 등록 빌더입니다. 문서 원천을 붙입니다.</summary>
	public sealed class NoticeServerBuilder
	{
		internal NoticeServerBuilder(IServiceCollection services)
		{
			Services = services;
		}

		public IServiceCollection Services { get; }

		/// <summary>
		/// 문서 파일을 원천으로 씁니다(싱글턴). <paramref name="path"/> 는 절대 경로이거나 콘텐츠 루트에 대한 상대 경로입니다.
		/// 파일은 엄격하게 읽고(모르는 속성 거절), <c>MapGeunedaNotice</c> 에서 처음 읽어 잘못되면 시작할 때 실패합니다.
		/// </summary>
		public NoticeServerBuilder AddFileSource(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentException("Path is empty", nameof(path));
			}

			Services.Replace(ServiceDescriptor.Singleton(provider => new NoticeFileSource(path,
				provider.GetRequiredService<IHostEnvironment>(), provider.GetRequiredService<TimeProvider>(),
				provider.GetRequiredService<ILogger<NoticeFileSource>>())));
			Services.Replace(ServiceDescriptor.Singleton<INoticeSource>(provider => provider.GetRequiredService<NoticeFileSource>()));
			return this;
		}

		/// <summary>게임이 구현한 원천을 씁니다. DB 컨텍스트를 받으려면 요청 범위(scoped)로 붙입니다.</summary>
		public NoticeServerBuilder AddSource<T>(ServiceLifetime lifetime = ServiceLifetime.Singleton) where T : class, INoticeSource
		{
			// 앞서 붙인 파일 원천은 쓰지 않으므로 시작 때 읽지도 않게 뺀다
			Services.RemoveAll<NoticeFileSource>();
			Services.Replace(ServiceDescriptor.Describe(typeof(INoticeSource), typeof(T), lifetime));
			return this;
		}
	}
}
