using System;
using Dalamud.Plugin.Ipc;

namespace MSBT.Ipc;

internal sealed class MsbtIpcProvider : IDisposable
{
    private readonly Plugin plugin;
    private readonly ICallGateProvider<string, string, int, bool> showAlertProvider;

    public MsbtIpcProvider(Plugin plugin)
    {
        this.plugin = plugin;
        showAlertProvider = Service.PluginInterface.GetIpcProvider<string, string, int, bool>("MSBT.ShowAlert");
        showAlertProvider.RegisterFunc(ShowExternalAlert);
    }

    private bool ShowExternalAlert(string message, string channelName, int soundId)
        => plugin.QueueIpcAlert(message, channelName, soundId);

    public void Dispose()
    {
        showAlertProvider.UnregisterFunc();
    }
}
