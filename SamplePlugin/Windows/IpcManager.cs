using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace MSBT;

public class IpcManager : IDisposable
{
    private readonly Plugin _plugin;
    private readonly ICallGateProvider<string, string, int, bool> _showAlertProvider;

    public IpcManager(Plugin plugin, IDalamudPluginInterface pi)
    {
        _plugin = plugin;
        _showAlertProvider = pi.GetIpcProvider<string, string, int, bool>("MSBT.ShowAlert");
        _showAlertProvider.RegisterFunc(ShowExternalAlert);
    }

    private bool ShowExternalAlert(string message, string channelName, int soundId)
    {
        var ch = _plugin.Configuration.Channels.FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
        if (ch == null || !ch.Enabled) return false;

        _plugin.Renderer.SpawnIpcAlert(message, ch, soundId);
        return true;
    }

    public void Dispose()
    {
        _showAlertProvider?.UnregisterFunc();
    }
}
