using HavalNeGovno.Services;
using HavalNeGovno.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Server;
public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup()
    {
        DotNetEnv.Env.TraversePath().Load();

        Configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
    }

    public IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        
        services.AddSingleton<IUrlParser>(provider =>
            new UrlParser(Configuration["BASE_URL"] ?? throw new InvalidOperationException("BASE_URL is not set"))
        );

        services.AddSingleton<IGeminiApi>(provider =>
            new GeminiApi(Configuration["GEMINI_API_KEY"] ?? throw new InvalidOperationException("GEMINI_API_KEY is not set"))
        );

        services.AddScoped<IDbService>(provider =>
            new DbService(
                $"Host={Configuration["DB_HOST"]};" +
                $"Port={Configuration["DB_PORT"]};" +
                $"Username={Configuration["DB_USER"]};" +
                $"Password={Configuration["DB_PASS"]};" +
                $"Database={Configuration["DB_NAME"]};"
            )
        );

        services.AddSingleton<ITranslator, MyMemoryTranslator>();

        services.AddSingleton<ILogger, Logger>();

        services.AddSingleton<IJsonServing, JsonServing>();

        services.AddSingleton<IGoogleImageSearchHelper>(provider =>
            new GoogleImageSearchHelper(
                Configuration["MY_SEARCH_API_KEY"] ?? throw new InvalidOperationException("MY_SEARCH_API_KEY is not set"),
                Configuration["MY_CX"] ?? throw new InvalidOperationException("MY_CX is not set"))
            );

        services.AddSingleton<SimpleServer>(provider =>
            new SimpleServer(
                "http://localhost:5252/",
                provider.GetRequiredService<IUrlParser>(),
                provider.GetRequiredService<IGeminiApi>(),
                provider.GetRequiredService<IDbService>(),
                provider.GetRequiredService<ITranslator>(),
                provider.GetRequiredService<ILogger>(),
                provider.GetRequiredService<IJsonServing>(),
                provider.GetRequiredService<IGoogleImageSearchHelper>()
            )
        );

        return services.BuildServiceProvider();
    }
}