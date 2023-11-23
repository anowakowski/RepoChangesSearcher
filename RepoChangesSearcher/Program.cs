using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RepoChangesSearcher.Core;
using Serilog;

IConfigurationBuilder builder = new ConfigurationBuilder();
builder
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var loggerConfiguration = new LoggerConfiguration()
                                .WriteTo.Console(
                                            restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information,
                                            outputTemplate: "{Timestamp:HH:mm:ss} [{Level}] {Message}{NewLine}{Exception}")
                                .WriteTo.File(
                                            "RepoChangesSearcherLog_.txt", 
                                            restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information,
                                            outputTemplate: "{Timestamp:HH:mm:ss} [{Level}] {Message}{NewLine}{Exception}",
                                            rollingInterval: RollingInterval.Hour)
                                .CreateLogger();

Log.Logger = loggerConfiguration;

var serviceProvider = new ServiceCollection()
    .AddLogging(builder => builder.AddSerilog(dispose: true))
    .AddSingleton<ISearcher, Searcher>()
    .AddSingleton<IConfiguration>(builder.Build())
    .BuildServiceProvider();


var searcher = serviceProvider.GetService<ISearcher>();

searcher.Search();

Console.WriteLine("Click any key to close");
Console.ReadKey();



