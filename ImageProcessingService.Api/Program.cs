using Imageflow.Server;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseImageflow(new ImageflowMiddlewareOptions()
    .SetMapWebRoot(true)
    .SetMyOpenSourceProjectUrl("https://github.com/doitsu2014/my-image-processing-service"));

// app.UseStaticFiles();
app.UseRouting();

app.MapGet("/", () => "Hello World!");
app.MapGet("/image-testing", async (context) =>
{
    context.Response.ContentType = "text/html";
    await context.Response.WriteAsync("<img src=\"Screenshot 2024-06-20 at 22.28.06.png?width=100\" />");
});

app.Run();