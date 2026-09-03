using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MSBT;

internal sealed class DrawHelper
{
    private readonly Configuration configuration;

    public DrawHelper(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public void DrawText(ImDrawListPtr drawList, Vector2 position, string text, uint color, uint effectColor, float _)
    {
        if (string.IsNullOrEmpty(text))
            return;

        float size = Math.Clamp(configuration.TextEffectSize, 0.5f, 8f);
        if (configuration.TextEffect == TextEffectType.Outline)
        {
            drawList.AddText(position + new Vector2(-size, -size), effectColor, text);
            drawList.AddText(position + new Vector2(size, -size), effectColor, text);
            drawList.AddText(position + new Vector2(-size, size), effectColor, text);
            drawList.AddText(position + new Vector2(size, size), effectColor, text);
            drawList.AddText(position + new Vector2(-size, 0), effectColor, text);
            drawList.AddText(position + new Vector2(size, 0), effectColor, text);
            drawList.AddText(position + new Vector2(0, -size), effectColor, text);
            drawList.AddText(position + new Vector2(0, size), effectColor, text);
        }
        else if (configuration.TextEffect == TextEffectType.Shadow)
        {
            drawList.AddText(position + new Vector2(size, size), effectColor, text);
        }

        drawList.AddText(position, color, text);
    }

    public static void DrawRadialCooldown(ImDrawListPtr drawList, Vector2 center, float radius, float progress, uint color)
    {
        if (progress <= 0.01f || !float.IsFinite(progress))
            return;

        drawList.PathClear();
        drawList.PathLineTo(center);
        drawList.PathArcTo(center, radius, -MathF.PI / 2f, (-MathF.PI / 2f) + (progress * MathF.Tau), 32);
        drawList.PathFillConvex(color);
    }

    public static void DrawAnchorCrosshair(Vector2 position, uint color, TextAlignment alignment, bool isCrit = false)
    {
        ImDrawListPtr drawList = ImGui.GetForegroundDrawList();
        drawList.AddCircleFilled(position, 4f, color);
        drawList.AddCircle(position, 5f, 0xFF000000, 12, 1.5f);
        uint alphaColor = (color & 0x00FFFFFF) | 0x88000000;
        drawList.AddLine(position - new Vector2(35, 0), position + new Vector2(35, 0), alphaColor);
        drawList.AddLine(position - new Vector2(0, 35), position + new Vector2(0, 35), alphaColor);

        if (isCrit)
            return;

        Vector2 start = position + new Vector2(0, 10);
        Vector2 end = start;
        if (alignment == TextAlignment.Center)
        {
            start.X -= 30;
            end.X += 30;
        }
        else if (alignment == TextAlignment.Right)
        {
            start.X -= 60;
        }
        else
        {
            end.X += 60;
        }

        drawList.AddLine(start, end, color, 3f);
        drawList.AddLine(start - new Vector2(0, 4), start + new Vector2(0, 4), color, 2f);
        drawList.AddLine(end - new Vector2(0, 4), end + new Vector2(0, 4), color, 2f);
    }
}
