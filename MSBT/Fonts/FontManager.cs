using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace MSBT;

internal readonly record struct FontSelection(IFontHandle Handle, float ScaleCorrection);

internal sealed class FontManager : IDisposable
{
    public const string EmbeddedFontKey = "MSBT Default (Defused)";
    public const string DalamudFontKey = "Dalamud Font";
    public const string GlobalFontKey = "Use Global Font";

    private const string GameFontPrefix = "FFXIV: ";
    private const string CustomFontPrefix = "Custom: ";
    private const int MaxCachedFontHandles = 32;

    private readonly record struct FontHandleKey(string Reference, int Size);

    private readonly IPluginLog log;
    private readonly IFontAtlas atlas;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Configuration configuration;
    private readonly Dictionary<FontHandleKey, IFontHandle> fontHandles = new();
    private readonly Queue<FontHandleKey> fontHandleOrder = new();
    private readonly Dictionary<string, string> customFontPaths = new(StringComparer.Ordinal);
    private readonly HashSet<FontHandleKey> failedFonts = new();
    private readonly HashSet<string> availableFontKeys = new(StringComparer.Ordinal);

    public string FontsDirectory => Path.Combine(pluginInterface.ConfigDirectory.FullName, "Fonts");
    public string[] FontOptions { get; private set; } = [EmbeddedFontKey, DalamudFontKey];
    public string[] ChannelFontOptions { get; private set; } = [GlobalFontKey, EmbeddedFontKey, DalamudFontKey];

    public FontManager(IPluginLog log, IFontAtlas atlas, IDalamudPluginInterface pluginInterface, Configuration configuration)
    {
        this.log = log;
        this.atlas = atlas;
        this.pluginInterface = pluginInterface;
        this.configuration = configuration;
        RefreshFonts();
    }

    public void RefreshFonts()
    {
        DisposeFontHandles();
        customFontPaths.Clear();
        failedFonts.Clear();

        try
        {
            Directory.CreateDirectory(FontsDirectory);
            AddFontsFromDirectory(FontsDirectory);
            AddFontsFromDirectory(pluginInterface.ConfigDirectory.FullName);
        }
        catch (Exception exception)
        {
            log.Warning(exception, "Failed to refresh MSBT fonts.");
        }

        var options = new List<string> { EmbeddedFontKey, DalamudFontKey };
        options.AddRange(Enum.GetValues<GameFontFamily>()
            .Where(static family => family != GameFontFamily.Undefined)
            .OrderBy(static family => family.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(static family => GameFontPrefix + family));
        options.AddRange(customFontPaths.Keys.Order(StringComparer.OrdinalIgnoreCase));

        FontOptions = options.ToArray();
        ChannelFontOptions = [GlobalFontKey, .. FontOptions];
        availableFontKeys.Clear();
        availableFontKeys.UnionWith(FontOptions);
        NormalizeConfiguration();
    }

    public void NormalizeConfiguration()
    {
        configuration.FontKey = NormalizeReference(configuration.FontKey, false);
        configuration.FontSize = NormalizeSize(configuration.FontSize);
        foreach (DisplayChannel channel in configuration.Channels)
        {
            channel.FontKey = NormalizeReference(channel.FontKey, true);
            channel.FontSize = channel.FontSize <= 0 ? 0 : NormalizeSize(channel.FontSize);
        }
    }

    public string NormalizeReference(string? reference, bool allowGlobal)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return allowGlobal ? string.Empty : EmbeddedFontKey;
        if (availableFontKeys.Contains(reference))
            return reference;

        string customKey = CustomFontPrefix + Path.GetFileName(reference);
        if (customFontPaths.ContainsKey(customKey))
            return customKey;

        if (reference.StartsWith("FFXIV_", StringComparison.OrdinalIgnoreCase))
        {
            string gameKey = GameFontPrefix + reference[6..];
            if (availableFontKeys.Contains(gameKey))
                return gameKey;
        }

        return allowGlobal ? string.Empty : EmbeddedFontKey;
    }

    public FontSelection GetChannelFont(string? channelFont, float channelFontSize)
    {
        string reference = string.IsNullOrWhiteSpace(channelFont) ? configuration.FontKey : channelFont;
        reference = NormalizeReference(reference, false);
        int size = (int)MathF.Round(NormalizeSize(channelFontSize > 0 ? channelFontSize : configuration.FontSize));

        if (reference == DalamudFontKey)
        {
            float defaultSize = pluginInterface.UiBuilder.FontDefaultSizePx;
            return new FontSelection(pluginInterface.UiBuilder.DefaultFontHandle, defaultSize > 0 ? size / defaultSize : 1f);
        }

        var key = new FontHandleKey(reference, size);
        if (fontHandles.TryGetValue(key, out IFontHandle? cached))
            return new FontSelection(cached, 1f);
        if (failedFonts.Contains(key))
            return GetEmbeddedFont(size);

        try
        {
            IFontHandle? handle = CreateFontHandle(key);
            if (handle != null)
            {
                CacheHandle(key, handle);
                return new FontSelection(handle, 1f);
            }
        }
        catch (Exception exception)
        {
            log.Warning(exception, "Failed to load font {Font} at {Size}px.", reference, size);
        }

        failedFonts.Add(key);
        return GetEmbeddedFont(size);
    }

    private FontSelection GetEmbeddedFont(int size)
    {
        var key = new FontHandleKey(EmbeddedFontKey, size);
        if (!fontHandles.TryGetValue(key, out IFontHandle? handle))
        {
            handle = CreateFontHandle(key)!;
            CacheHandle(key, handle);
        }

        return new FontSelection(handle, 1f);
    }

    private IFontHandle? CreateFontHandle(FontHandleKey key)
    {
        if (key.Reference == EmbeddedFontKey)
        {
            return atlas.NewDelegateFontHandle(builder => builder.OnPreBuild(toolkit =>
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MSBT.Fonts.defused.ttf");
                if (stream != null)
                    toolkit.AddFontFromStream(stream, new SafeFontConfig { SizePx = key.Size }, false, "MSBT-Defused");
                else
                    log.Error("Failed to load the embedded MSBT font.");
            }));
        }

        if (key.Reference.StartsWith(GameFontPrefix, StringComparison.Ordinal) &&
            Enum.TryParse(key.Reference[GameFontPrefix.Length..], out GameFontFamily family) &&
            family != GameFontFamily.Undefined)
            return atlas.NewGameFontHandle(new GameFontStyle(family, key.Size));

        if (!customFontPaths.TryGetValue(key.Reference, out string? path) || !File.Exists(path))
            return null;

        return atlas.NewDelegateFontHandle(builder =>
            builder.OnPreBuild(toolkit => toolkit.AddFontFromFile(path, new SafeFontConfig { SizePx = key.Size })));
    }

    private void AddFontsFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                customFontPaths.TryAdd(CustomFontPrefix + Path.GetFileName(path), path);
        }
    }

    private static float NormalizeSize(float size) => Math.Clamp(float.IsFinite(size) ? size : 36f, 8f, 96f);

    private void CacheHandle(FontHandleKey key, IFontHandle handle)
    {
        while (fontHandles.Count >= MaxCachedFontHandles && fontHandleOrder.TryDequeue(out FontHandleKey expired))
        {
            if (fontHandles.Remove(expired, out IFontHandle? expiredHandle))
                expiredHandle.Dispose();
        }

        fontHandles[key] = handle;
        fontHandleOrder.Enqueue(key);
    }

    private void DisposeFontHandles()
    {
        foreach (IFontHandle handle in fontHandles.Values)
            handle.Dispose();
        fontHandles.Clear();
        fontHandleOrder.Clear();
    }

    public void Dispose()
    {
        DisposeFontHandles();
        customFontPaths.Clear();
        failedFonts.Clear();
        availableFontKeys.Clear();
    }
}
