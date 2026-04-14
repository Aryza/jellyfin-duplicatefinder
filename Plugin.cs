using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.DuplicateFinder.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DuplicateFinder;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance    = this;
        ResultsPath = Path.Combine(applicationPaths.DataPath, "duplicatefinder_results.json");
    }

    public static Plugin? Instance    { get; private set; }
    public static string   ResultsPath { get; private set; } = string.Empty;

    public override string Name        => "DuplicateFinder";
    public override Guid   Id          => Guid.Parse("2d0aac3c-29b6-48e7-a445-a34c79291899");
    public override string Description => "Detects duplicate movies, TV episodes, and music tracks.";

    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        // Single unified page — serves both as the Dashboard > Plugins settings target
        // and as a sidebar entry (via EnableInMainMenu). The page itself renders two tabs
        // (Settings & Scan / Report).
        // Do NOT set DisplayName, MenuSection, or MenuIcon — they do not exist in the server
        // binary even though NuGet compiles them fine, causing a load crash.
        new PluginPageInfo
        {
            Name                 = Name,
            EnableInMainMenu     = true,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        }
    };
}
