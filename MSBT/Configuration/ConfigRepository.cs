using System;
using System.Diagnostics;

namespace MSBT;

internal static class ConfigRepository
{
    private static readonly object SaveGate = new();
    private static readonly long SaveDelayTicks = Stopwatch.Frequency / 4;
    private static Configuration? pendingConfiguration;
    private static long lastSaveRequest;

    public static Configuration LoadOrDefault()
        => Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

    public static void Save(Configuration configuration)
    {
        lock (SaveGate)
        {
            pendingConfiguration = configuration;
            lastSaveRequest = Stopwatch.GetTimestamp();
        }
    }

    public static void FlushPending()
    {
        Configuration? configuration;
        lock (SaveGate)
        {
            if (pendingConfiguration == null || Stopwatch.GetTimestamp() - lastSaveRequest < SaveDelayTicks)
                return;

            configuration = pendingConfiguration;
            pendingConfiguration = null;
        }

        Service.PluginInterface.SavePluginConfig(configuration);
    }

    public static void SaveImmediate(Configuration configuration)
    {
        lock (SaveGate)
            pendingConfiguration = null;
        Service.PluginInterface.SavePluginConfig(configuration);
    }
}
