using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.S3;
using Azure.Storage.Blobs;
using Imageflow.Fluent;
using Imageflow.Server;
using Imageflow.Server.HybridCache;
using Imageflow.Server.Storage.AzureBlob;
using Imageflow.Server.Storage.RemoteReader;
using Imageflow.Server.Storage.S3;
using ImageProcessingService.Api.Services;

namespace ImageProcessingService.Api.Extensions;

public static class HostingExtensions
{
    public static WebApplicationBuilder Initialize(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        var defaultOption = builder.Configuration.GetAWSOptions();
        defaultOption.Credentials = new BasicAWSCredentials(builder.Configuration.GetValue<string>("AWS:AccessKey"), builder.Configuration.GetValue<string>("AWS:Secret"));
        
        services.AddAWSService<IAmazonS3>(defaultOption);

        services.AddControllers();

        // See the README in src/Imageflow.Server.Storage.RemoteReader/ for more advanced configuration
        services.AddHttpClient();
        // To add the RemoteReaderService, you need to all .AddHttpClient() first
        services.AddImageflowRemoteReaderService(new RemoteReaderServiceOptions
            {
                SigningKey = "MySecret#123456789"
            }
            .AddPrefix("/remote/"));

        // Make S3 containers available at /ri/ and /imageflow-resources/
        // If you use credentials, do not check them into your repository
        // You can call AddImageflowS3Service multiple times for each unique access key
        services.AddImageflowS3Service(new S3ServiceOptions()
            .MapPrefix("/s3/", "bucket-dzsydl")
        );

        // Make Azure container available at /azure
        // You can call AddImageflowAzureBlobService multiple times for each connection string
        // services.AddImageflowAzureBlobService(
        //     new AzureBlobServiceOptions(
        //             "UseDevelopmentStorage=true;",
        //             new BlobClientOptions())
        //         .MapPrefix("/azure", "imageflow-demo"));

        // Custom blob services can do whatever you need. See CustomBlobService.cs in src/Imageflow.Service.Example
        services.AddImageflowCustomBlobService(new CustomBlobServiceOptions()
        {
            Prefix = "/custom_blobs/",
            IgnorePrefixCase = true,
            ConnectionString = "UseDevelopmentStorage=true;",
            // Only allow 'my_container' to be accessed. /custom_blobs/my_container/key.jpg would be an example path.
            ContainerKeyFilterFunction = (container, key) =>
                container == "my_container" ? Tuple.Create(container, key) : null
        });

        var homeFolder = (Environment.OSVersion.Platform == PlatformID.Unix ||
                          Environment.OSVersion.Platform == PlatformID.MacOSX)
            ? Environment.GetEnvironmentVariable("HOME")
            : Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");


        // You can add a hybrid cache (in-memory persisted database for tracking filenames, but files used for bytes)
        // But remember to call ImageflowMiddlewareOptions.SetAllowCaching(true) for it to take effect
        // If you're deploying to azure, provide a disk cache folder *not* inside ContentRootPath
        // to prevent the app from recycling whenever folders are created.
        services.AddImageflowHybridCache(
            new HybridCacheOptions(Path.Combine(homeFolder, "imageflow_example_hybrid_cache"))
            {
                // How long after a file is created before it can be deleted
                MinAgeToDelete = TimeSpan.FromSeconds(10),
                // How much RAM to use for the write queue before switching to synchronous writes
                WriteQueueMemoryMb = 100,
                // The maximum size of the cache (1GB)
                CacheSizeMb = 1000,
            });

        return builder;
    }

    public static WebApplication UseDefaultConfig(this WebApplication app)
    {
        var config = app.Configuration;
        var env = app.Environment;

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        // You have a lot of configuration options
        app.UseImageflow(new ImageflowMiddlewareOptions()
            // Maps / to WebRootPath
            .SetMapWebRoot(true)
            .SetMyOpenSourceProjectUrl("https://github.com/doitsu2014/my-image-processing-service")
            // Maps /folder to WebRootPath/folder
            .MapPath("/folder", Path.Combine(env.ContentRootPath, "folder"))
            // Allow localhost to access the diagnostics page or remotely via /imageflow.debug?password=fuzzy_caterpillar
            .SetDiagnosticsPageAccess(env.IsDevelopment()
                ? AccessDiagnosticsFrom.AnyHost
                : AccessDiagnosticsFrom.LocalHost)
            .SetDiagnosticsPagePassword("fuzzy_caterpillar")
            // Allow HybridCache or other registered IStreamCache to run
            .SetAllowCaching(true)
            // Cache publicly (including on shared proxies and CDNs) for 30 days
            .SetDefaultCacheControlString("public, max-age=2592000")
            // Allows extensionless images to be served within the given directory(ies)
            .HandleExtensionlessRequestsUnder("/custom_blobs/", StringComparison.OrdinalIgnoreCase)
            // Force all paths under "/gallery" to be watermarked
            .AddRewriteHandler("/gallery", args => { args.Query["watermark"] = "imazen"; })
            .AddCommandDefault("down.filter", "mitchell")
            .AddCommandDefault("f.sharpen", "15")
            .AddCommandDefault("webp.quality", "90")
            .AddCommandDefault("ignore_icc_errors", "true")
            //When set to true, this only allows ?preset=value URLs, returning 403 if you try to use any other commands. 
            .SetUsePresetsExclusively(false)
            .AddPreset(new PresetOptions("large", PresetPriority.DefaultValues)
                .SetCommand("width", "1024")
                .SetCommand("height", "1024")
                .SetCommand("mode", "max"))
            // When set, this only allows urls with a &signature, returning 403 if missing/invalid. 
            // Use Imazen.Common.Helpers.Signatures.SignRequest(string pathAndQuery, string signingKey) to generate
            //.ForPrefix allows you to set less restrictive rules for subfolders. 
            // For example, you may want to allow unmodified requests through with SignatureRequired.ForQuerystringRequests
            // .SetRequestSignatureOptions(
            //     new RequestSignatureOptions(SignatureRequired.ForAllRequests, new []{"test key"})
            //         .ForPrefix("/logos/", StringComparison.Ordinal, 
            //             SignatureRequired.ForQuerystringRequests, new []{"test key"}))
            // It's a good idea to limit image sizes for security. Requests causing these to be exceeded will fail
            // The last argument to FrameSizeLimit() is the maximum number of megapixels
            .SetJobSecurityOptions(new SecurityOptions()
                .SetMaxDecodeSize(new FrameSizeLimit(8000, 8000, 40))
                .SetMaxFrameSize(new FrameSizeLimit(8000, 8000, 40))
                .SetMaxEncodeSize(new FrameSizeLimit(8000, 8000, 20)))
            // Register a named watermark that floats 10% from the bottom-right corner of the image
            // With 70% opacity and some sharpness applied. 
            .AddWatermark(
                new NamedWatermark("imazen",
                    "/images/imazen_400.png",
                    new WatermarkOptions()
                        .SetFitBoxLayout(
                            new WatermarkFitBox(WatermarkAlign.Image, 10, 10, 90, 90),
                            WatermarkConstraintMode.Within,
                            new ConstraintGravity(100, 100))
                        .SetOpacity(0.7f)
                        .SetHints(
                            new ResampleHints()
                                .SetResampleFilters(InterpolationFilter.Robidoux_Sharp, null)
                                .SetSharpen(7, SharpenWhen.Downscaling))
                        .SetMinCanvasSize(200, 150)))
            .AddWatermarkingHandler("/", args =>
            {
                if (args.Query.TryGetValue("water", out var value) && value == "mark")
                {
                    args.AppliedWatermarks.Add(new NamedWatermark(null, "/images/imazen_400.png",
                        new WatermarkOptions()));
                }
            }));


        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthorization();
        app.MapGet("/", async (context) =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("<img src=\"/s3/image01.png?width=850\" /><br/><img src=\"/s3/fire-umbrella-small.jpg?width=850\" /><br/><img src=\"/s3/image02.png?width=850\" />");
        });


        return app;
    }
}