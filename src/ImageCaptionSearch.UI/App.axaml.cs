using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ImageCaptionSearch.UI.ViewModels;
using ImageCaptionSearch.UI.Views;
using ImageCaptionSearch.Core.Services;
using ImageCaptionSearch.Core.Interfaces;
using Serilog;
using Microsoft.Extensions.Logging;

namespace ImageCaptionSearch.UI;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        
        // Setup Logging
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageCaptionSearch");
        Directory.CreateDirectory(appDataPath);
        var logPath = Path.Combine(appDataPath, "logs", "log-.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        services.AddLogging(lb => lb.AddSerilog());

        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(ServiceCollection services)
    {
        // Infrastructure
        services.AddHttpClient<ILmStudioClient, LmStudioClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    // For local development with LM Studio, sometimes timeouts are long
                    // but we handle them via settings.
                });

        // Core Services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILibraryRegistryService, LibraryRegistryService>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IIndexingPipelineService, IndexingPipelineService>();
        services.AddSingleton<ICaptionService, CaptionService>();
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IFaceRecognitionService, FaceRecognitionService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
    }



    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}