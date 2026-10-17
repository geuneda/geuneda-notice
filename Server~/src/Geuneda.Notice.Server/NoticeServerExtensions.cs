using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Geuneda.Notice.Server
{
	/// <summary>공지 배포 등록 확장입니다.</summary>
	public static class NoticeServerExtensions
	{
		/// <summary>
		/// 공지 배포를 등록합니다. 돌려준 빌더로 원천(<see cref="NoticeServerBuilder.AddFileSource"/>, <see cref="NoticeServerBuilder.AddSource{T}"/>)을 붙이고,
		/// 엔드포인트는 <c>app.MapGeunedaNotice()</c> 로 연결합니다.
		/// </summary>
		/// <example>
		/// <code>
		/// builder.Services.AddGeunedaNotice().AddFileSource("notices.json");
		/// app.MapGeunedaNotice("/notices");
		/// </code>
		/// </example>
		public static NoticeServerBuilder AddGeunedaNotice(this IServiceCollection services, Action<NoticeServerOptions> configure = null)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			var options = services.AddOptions<NoticeServerOptions>();

			if (configure != null)
			{
				options.Configure(configure);
			}

			options.Validate(o => !string.IsNullOrWhiteSpace(o.CacheControl), "NoticeServerOptions.CacheControl is required").ValidateOnStart();
			services.TryAddSingleton(TimeProvider.System);
			services.TryAddSingleton<NoticePublisher>();
			return new NoticeServerBuilder(services);
		}

		/// <summary>
		/// 지금 시각(<see cref="TimeProvider"/>)에 보이는 공지만 남긴 문서를 GET 으로 내려줍니다(아직 시작하지 않은 공지와 끝난 공지는 뺌).
		/// 응답에 ETag(문서 바이트의 SHA-256)와 Cache-Control 을 붙이고, <c>If-None-Match</c> 가 맞으면 304 입니다.
		/// 파일 원천이면 여기서 처음 읽어 잘못되면 예외를 던집니다.
		/// </summary>
		/// <exception cref="InvalidOperationException">AddGeunedaNotice 나 원천을 등록하지 않았거나, 파일 원천의 문서를 읽거나 검증하지 못한 경우</exception>
		public static RouteHandlerBuilder MapGeunedaNotice(this IEndpointRouteBuilder endpoints, string pattern = "/notices")
		{
			if (endpoints == null)
			{
				throw new ArgumentNullException(nameof(endpoints));
			}

			if (string.IsNullOrEmpty(pattern))
			{
				throw new ArgumentException("Pattern is empty", nameof(pattern));
			}

			var services = endpoints.ServiceProvider;
			var registry = services.GetRequiredService<IServiceProviderIsService>();

			if (!registry.IsService(typeof(NoticePublisher)))
			{
				throw new InvalidOperationException("Call services.AddGeunedaNotice() before MapGeunedaNotice");
			}

			if (!registry.IsService(typeof(INoticeSource)))
			{
				throw new InvalidOperationException("Add a notice source (AddFileSource or AddSource) before MapGeunedaNotice");
			}

			// 파일 원천은 지금 읽어 잘못된 파일을 시작할 때 드러낸다(게임 원천은 DB 가 준비되기 전일 수 있어 읽지 않음)
			if (registry.IsService(typeof(NoticeFileSource)))
			{
				services.GetRequiredService<NoticeFileSource>().Load();
			}

			Func<HttpContext, Task> handler = WriteAsync;
			return endpoints.MapGet(pattern, handler);
		}

		private static async Task WriteAsync(HttpContext context)
		{
			var services = context.RequestServices;
			var document = await services.GetRequiredService<INoticeSource>().GetDocumentAsync(context.RequestAborted) ??
				throw new InvalidOperationException("Notice source returned a null document");
			var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
			var snapshot = services.GetRequiredService<NoticePublisher>().Publish(document, now);
			var response = context.Response;
			response.Headers.ETag = snapshot.ETag;
			response.Headers.CacheControl = services.GetRequiredService<IOptions<NoticeServerOptions>>().Value.CacheControl;

			if (Matches(context.Request.Headers.IfNoneMatch, snapshot.ETag))
			{
				response.StatusCode = StatusCodes.Status304NotModified;
				return;
			}

			response.ContentType = "application/json; charset=utf-8";
			response.ContentLength = snapshot.Bytes.Length;
			await response.Body.WriteAsync(snapshot.Bytes, context.RequestAborted);
		}

		private static bool Matches(StringValues ifNoneMatch, string etag)
		{
			// If-None-Match 는 약한 비교(RFC 9110 13.1.2). 프록시가 W/ 를 붙여도 같은 문서로 본다
			if (StringValues.IsNullOrEmpty(ifNoneMatch) || !EntityTagHeaderValue.TryParseList(ifNoneMatch, out var tags))
			{
				return false;
			}

			var current = EntityTagHeaderValue.Parse(etag);
			return tags.Any(tag => tag.Tag.Equals("*") || tag.Compare(current, false));
		}
	}
}
