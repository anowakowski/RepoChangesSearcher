using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RepoChangesSearcher.Core;

IConfigurationBuilder builder = new ConfigurationBuilder();
builder
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var serviceProvider = new ServiceCollection()
    .AddLogging()
    .AddSingleton<ISearcher, Searcher>()
    .AddSingleton<IConfiguration>(builder.Build())
    .BuildServiceProvider();


var searcher = serviceProvider.GetService<ISearcher>();

searcher.Search();



