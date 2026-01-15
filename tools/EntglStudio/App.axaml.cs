using Avalonia.Markup.Xaml;
using Lifter.Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntglStudio;

public class App : HostedApplication<MainView>
{
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure window settings
        services.ConfigureWindow(config =>
        {
            config.Title = "EntglStudio - Data Manager";
            config.Width = 1024;
            config.Height = 768;
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddConfiguration(configuration.GetSection("Logging"));
        });
    }
}