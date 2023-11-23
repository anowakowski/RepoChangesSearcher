using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepoChangesSearcher;
using RepoChangesSearcher.Core;

IConfigurationBuilder builder = new ConfigurationBuilder();
builder
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddFilter("Microsoft", LogLevel.Warning)
        .AddFilter("System", LogLevel.Warning)
        .AddFilter("NonHostConsoleApp.Program", LogLevel.Debug)
        .AddSimpleConsole(options =>
        {
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        })
        .AddDebug()
        .SetMinimumLevel(LogLevel.Debug);
});

var serviceProvider = new ServiceCollection()
    .AddLogging()
    .AddSingleton<ISearcher, Searcher>()
    .AddSingleton<IConfiguration>(builder.Build())
    .AddSingleton<ILogger>(loggerFactory.CreateLogger<AppLoggin>())
    .BuildServiceProvider();


var searcher = serviceProvider.GetService<ISearcher>();

searcher.Search();

Console.ReadKey();



