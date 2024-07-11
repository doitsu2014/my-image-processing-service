using Imageflow.Server;
using ImageProcessingService.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Initialize();

var app = builder.Build();

app.UseDefaultConfig();
app.Run();