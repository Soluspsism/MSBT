using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using MSBT.Ipc;
using MSBT.Windows;

namespace MSBT;

public sealed class Plugin : IAsyncDalamudPlugin
{
    private const string CommandName = "/msbt";
    private const int MaxPooledTextNodes = 512;
    private const int MaxQueuedAlerts = 1024;
    private const int MaxAlertsPerFrame = 64;
    private const int MaxAlertLength = 4096;

    private readonly record struct IpcAlert(string Message, string ChannelName, int SoundId);

    private bool commandRegistered;
    private bool drawRegistered;
    private bool openMainRegistered;
    private bool openConfigRegistered;
    private bool isLoaded;
    private volatile bool isDisposed;

    internal Configuration Configuration { get; private set; } = null!;
    internal WindowSystem WindowSystem { get; private set; } = null!;
    internal ConfigWindow ConfigWindow { get; private set; } = null!;
    internal FontManager FontManager { get; private set; } = null!;
    internal CombatParser Parser { get; private set; } = null!;
    internal Renderer Renderer { get; private set; } = null!;
    internal MsbtIpcProvider IpcProvider { get; private set; } = null!;

    internal bool IsEditMode;
    internal List<CustomSCTNode> CustomTexts { get; } = new();
    internal object TextNodesGate { get; } = new();
    private readonly Stack<CustomSCTNode> textNodePool = new();
    private readonly Queue<IpcAlert> queuedAlerts = new();
    private readonly object ipcGate = new();

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (isDisposed)
            throw new ObjectDisposedException(nameof(Plugin));
        if (isLoaded)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        await Service.Framework.RunOnFrameworkThread(() => Load(cancellationToken));
    }

    private void Load(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Configuration = ConfigRepository.LoadOrDefault();
            Configuration.EnsureInitialized(ImGui.GetMainViewport().Size);

            WindowSystem = new WindowSystem("MSBT");
            FontManager = new FontManager(Service.Log, Service.PluginInterface.UiBuilder.FontAtlas, Service.PluginInterface, Configuration);
            ConfigRepository.SaveImmediate(Configuration);
            Renderer = new Renderer(this);
            Parser = new CombatParser(this);
            IpcProvider = new MsbtIpcProvider(this);

            ConfigWindow = new ConfigWindow(this);
            WindowSystem.AddWindow(ConfigWindow);

            Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open MSBT Settings.",
                ShowInHelp = true,
            });
            commandRegistered = true;

            Service.PluginInterface.UiBuilder.Draw += DrawUi;
            drawRegistered = true;
            Service.PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;
            openMainRegistered = true;
            Service.PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
            openConfigRegistered = true;

            isLoaded = true;
        }
        catch
        {
            DisposeResources();
            isDisposed = true;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        var framework = Service.Framework;
        await framework.RunOnFrameworkThread(DisposeResources);
    }

    private void DisposeResources()
    {
        if (drawRegistered)
        {
            Cleanup(() => Service.PluginInterface.UiBuilder.Draw -= DrawUi, "unregister draw callback");
            drawRegistered = false;
        }

        if (openConfigRegistered)
        {
            Cleanup(() => Service.PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi, "unregister config callback");
            openConfigRegistered = false;
        }

        if (openMainRegistered)
        {
            Cleanup(() => Service.PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi, "unregister main UI callback");
            openMainRegistered = false;
        }

        Cleanup(() => IpcProvider?.Dispose(), "dispose IPC provider");
        Cleanup(() => Parser?.Dispose(), "dispose combat parser");

        if (commandRegistered)
        {
            Cleanup(() => Service.CommandManager.RemoveHandler(CommandName), "remove command handler");
            commandRegistered = false;
        }

        Cleanup(() => WindowSystem?.RemoveAllWindows(), "remove windows");
        Cleanup(() => FontManager?.Dispose(), "dispose fonts");

        if (Configuration is not null)
            Cleanup(() => ConfigRepository.SaveImmediate(Configuration), "save configuration");

        lock (TextNodesGate)
        {
            CustomTexts.Clear();
            textNodePool.Clear();
        }
        lock (ipcGate)
            queuedAlerts.Clear();

        isLoaded = false;
        Service.Clear();
    }

    private static void Cleanup(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Service.Log.Error(exception, "Failed to {Operation} during MSBT shutdown.", operation);
        }
    }

    private void DrawUi()
    {
        ConfigRepository.FlushPending();
        DrainIpcAlerts();
        Renderer.Draw();
    }

    private void OnCommand(string command, string args) => ToggleConfigUi();

    internal CustomSCTNode AcquireTextNode()
    {
        lock (TextNodesGate)
        {
            var node = textNodePool.Count > 0 ? textNodePool.Pop() : new CustomSCTNode();
            CustomTexts.Add(node);
            return node;
        }
    }

    internal void ReleaseTextNodeAt(int index)
    {
        lock (TextNodesGate)
        {
            CustomSCTNode node = CustomTexts[index];
            CustomTexts.RemoveAt(index);
            node.Reset();
            if (textNodePool.Count < MaxPooledTextNodes)
                textNodePool.Push(node);
        }
    }

    internal void ToggleConfigUi()
    {
        if (ConfigWindow.IsOpen)
            IsEditMode = false;

        ConfigWindow.IsOpen = !ConfigWindow.IsOpen;
    }

    internal void ImportConfiguration(string base64)
    {
        if (!Configuration.ImportFromBase64(base64))
            return;

        FontManager.NormalizeConfiguration();
        ConfigRepository.SaveImmediate(Configuration);
    }

    internal bool QueueIpcAlert(string message, string channelName, int soundId)
    {
        if (string.IsNullOrWhiteSpace(channelName))
            return false;

        lock (ipcGate)
        {
            if (isDisposed || queuedAlerts.Count >= MaxQueuedAlerts)
                return false;

            string safeMessage = message?.Length > MaxAlertLength ? message[..MaxAlertLength] : message ?? string.Empty;
            queuedAlerts.Enqueue(new IpcAlert(safeMessage, channelName, soundId));
            return true;
        }
    }

    private void DrainIpcAlerts()
    {
        for (int i = 0; i < MaxAlertsPerFrame; i++)
        {
            IpcAlert alert;
            lock (ipcGate)
            {
                if (queuedAlerts.Count == 0)
                    break;
                alert = queuedAlerts.Dequeue();
            }

            foreach (DisplayChannel channel in Configuration.Channels)
            {
                if (channel.Enabled && channel.Name.Equals(alert.ChannelName, StringComparison.OrdinalIgnoreCase))
                {
                    Renderer.SpawnIpcAlert(alert.Message, channel, alert.SoundId);
                    break;
                }
            }
        }
    }

    internal void SpawnTestText(bool isCrit, DisplayChannel channel, bool isHeal = false, bool isAlert = false)
        => Renderer.SpawnTestText(isCrit, channel, isHeal, isAlert);
}
