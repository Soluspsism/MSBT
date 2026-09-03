using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace MSBT;

public class FontManager : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IFontAtlas _atlas;
    private readonly IDalamudPluginInterface _pi;
    private readonly Configuration _cfg;

    public IFontHandle DefaultFont { get; private set; }
    private readonly Dictionary<string, IFontHandle> _customFonts = new();

    public FontManager(IPluginLog log, IFontAtlas atlas, IDalamudPluginInterface pi, Configuration cfg)
    {
        _log = log;
        _atlas = atlas;
        _pi = pi;
        _cfg = cfg;

        DefaultFont = atlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(step =>
            {
                var fontConfig = new SafeFontConfig { SizePx = 36.0f };

                string resourceName = "MSBT.Fonts.defused.ttf";
                var fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (fontStream != null)
                {
                    step.AddFontFromStream(fontStream, fontConfig, false, "MSBT-Defused");
                    _log.Information("Embedded font defused.ttf successfully loaded!");
                }
                else
                {
                    _log.Error($"Failed to load embedded font '{resourceName}'.");
                }
            });
        });
    }

    public IFontHandle GetChannelFont(string channelFontFileName)
    {
        string targetFont = string.IsNullOrWhiteSpace(channelFontFileName) ? _cfg.FontFileName : channelFontFileName;

        if (string.IsNullOrWhiteSpace(targetFont))
            return DefaultFont;

        if (_customFonts.TryGetValue(targetFont, out var handle))
            return handle;

        var newFont = _atlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(step =>
            {
                var fontConfig = new SafeFontConfig { SizePx = 36.0f };
                string customPath = Path.Combine(_pi.ConfigDirectory.FullName, targetFont);

                if (File.Exists(customPath))
                {
                    step.AddFontFromFile(customPath, fontConfig);
                    _log.Information($"Custom font successfully loaded from: {customPath}");
                }
                else
                {
                    _log.Warning($"Font not found at path: {customPath}. Falling back to default font.");
                }
            });
        });

        _customFonts[targetFont] = newFont;
        return newFont;
    }

    public void Dispose()
    {
        DefaultFont?.Dispose();
        foreach (var font in _customFonts.Values)
        {
            font?.Dispose();
        }
        _customFonts.Clear();
    }
}
