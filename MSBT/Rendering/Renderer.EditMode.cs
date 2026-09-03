using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MSBT;

internal sealed partial class Renderer
{
    private void DrawEditMode()
    {
        var bgDrawList = ImGui.GetBackgroundDrawList();
        Vector2 screenSize = ImGui.GetIO().DisplaySize;
        float centerX = screenSize.X / 2f;
        float centerY = screenSize.Y / 2f;

        uint minorGridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.04f));
        uint majorGridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.12f));
        uint centerColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0f, 0f, 0.4f));
        uint critLinkColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 0.5f));
        uint critAnchorColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 0.9f));
        uint critTextColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 1f));

        int numCols = (int)(centerX / 25f) + 1;
        for (int i = 0; i <= numCols; i++)
        {
            float offset = i * 25f;
            uint color = i % 4 == 0 ? majorGridColor : minorGridColor;
            bgDrawList.AddLine(new Vector2(centerX + offset, 0), new Vector2(centerX + offset, screenSize.Y), color, 1f);
            if (i > 0)
                bgDrawList.AddLine(new Vector2(centerX - offset, 0), new Vector2(centerX - offset, screenSize.Y), color, 1f);
        }

        int numRows = (int)(centerY / 25f) + 1;
        for (int i = 0; i <= numRows; i++)
        {
            float offset = i * 25f;
            uint color = i % 4 == 0 ? majorGridColor : minorGridColor;
            bgDrawList.AddLine(new Vector2(0, centerY + offset), new Vector2(screenSize.X, centerY + offset), color, 1f);
            if (i > 0)
                bgDrawList.AddLine(new Vector2(0, centerY - offset), new Vector2(screenSize.X, centerY - offset), color, 1f);
        }

        bgDrawList.AddLine(new Vector2(centerX, 0), new Vector2(centerX, screenSize.Y), centerColor, 2f);
        bgDrawList.AddLine(new Vector2(0, centerY), new Vector2(screenSize.X, centerY), centerColor, 2f);

        foreach (DisplayChannel channel in plugin.Configuration.Channels)
        {
            if (!channel.Enabled)
                continue;

            ImGui.SetNextWindowPos(new Vector2(channel.X, channel.Y), ImGuiCond.Appearing);
            using (ImRaii.PushColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.05f, 0.8f)))
            {
                bool visible = ImGui.Begin($"MSBT_Anchor_{channel.Name}", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
                try
                {
                    if (visible)
                    {
                        string icon = channel.Mode == ChannelMode.Overlay ? "[Ovl]" : channel.Mode == ChannelMode.Tracker ? "[Trk]" : "✥";
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), icon);
                        ImGui.SameLine();
                        ImGui.Text(channel.Name);

                        if (ImGui.IsWindowFocused())
                        {
                            Vector2 position = ImGui.GetWindowPos();
                            channel.X = position.X;
                            channel.Y = position.Y;
                        }
                        else
                        {
                            ImGui.SetWindowPos(new Vector2(channel.X, channel.Y));
                        }

                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                            plugin.Configuration.Save();
                    }
                }
                finally
                {
                    ImGui.End();
                }
            }

            DrawHelper.DrawAnchorCrosshair(new Vector2(channel.X, channel.Y), ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 1f, 0.4f, 1f)), channel.Alignment);

            if (channel.CritBehavior != 0 && channel.Mode == ChannelMode.Scrolling)
            {
                bgDrawList.AddLine(new Vector2(channel.X, channel.Y), new Vector2(channel.X + channel.CritOffsetX, channel.Y + channel.CritOffsetY), critLinkColor, 2f);
                DrawHelper.DrawAnchorCrosshair(new Vector2(channel.X + channel.CritOffsetX, channel.Y + channel.CritOffsetY), critAnchorColor, TextAlignment.Center, true);
                ImGui.GetForegroundDrawList().AddText(new Vector2(channel.X + channel.CritOffsetX + 10, channel.Y + channel.CritOffsetY - 15), critTextColor, "Crits");
            }
        }
    }
}
