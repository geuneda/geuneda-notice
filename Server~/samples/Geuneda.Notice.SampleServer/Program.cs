using Geuneda.Notice.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

// 실행: dotnet run --project "Server~/samples/Geuneda.Notice.SampleServer" --no-launch-profile -- --urls http://127.0.0.1:5599
// Unity 상호 운용 테스트: GEUNEDA_NOTICE_INTEROP_URL=http://127.0.0.1:5599/
// notices.json 은 실행 중에 고쳐도 1초 안에 다시 읽는다(잘못된 파일이면 오류 로그를 남기고 이전 문서를 계속 내려줌).
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGeunedaNotice().AddFileSource("notices.json");

var app = builder.Build();
app.MapGeunedaNotice("/notices");
app.Run();
