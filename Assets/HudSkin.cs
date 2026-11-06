using System.Collections.Generic;
using UnityEngine;

// the look shared by the Dashboard and the LapScreen: colors, text styles, textures, and a few
// drawing helpers. uses Unity's built-in immediate-mode GUI (OnGUI), which needs no packages:
// this was definitely the harder approach when it comes to making a hud but its just so people dont have to download
// any additional packages or files 
//
// layout is in "units": the screen is always 1080 units tall (and as wide as its shape makes it),
// so the HUD looks the same at any resolution. rects and font sizes are turned into real pixels
// right before drawing, so text is rendered at full resolution and stays sharp.
public static class HudSkin
{
    public static readonly Color Gold = Hex(0xFDC700);        // UCSC gold
    public static readonly Color Orange = Hex(0xFF8A00);
    public static readonly Color Red = Hex(0xFF4D4D);
    public static readonly Color Green = Hex(0x34D17A);
    public static readonly Color Blue = Hex(0x4C9BFF);
    public static readonly Color TextMain = Hex(0xF4F6F8);
    public static readonly Color TextDim = Hex(0x9AA3AE);
    public static readonly Color Track = new Color(1f, 1f, 1f, 0.12f);   // empty part of bars and arcs

    public static float K { get; private set; } = 1f;      // real pixels per unit
    public static float W { get; private set; } = 1920f;   // screen width in units
    public const float H = 1080f;

    // call at the top of every OnGUI that uses the skin.
    public static void Begin()
    {
        K = Mathf.Max(0.1f, Screen.height / H);
        W = Screen.width / K;
        MakeTextures();
    }

    public static Rect R(float x, float y, float w, float h) => new Rect(x * K, y * K, w * K, h * K);
    public static Vector2 P(float x, float y) => new Vector2(x * K, y * K);

    public static Texture2D Panel, Pill, Segment, Disc, Glow, Wheel, Pixel;
    static Texture2D buttonNormal, buttonHover, buttonActive;
    const int PanelBorder = 18;

    static void MakeTextures()
    {
        if (Panel != null) return;
        Panel = ToTexture(HudArt.Panel(64, 18, HudArt.Rgba.Hex(0x0B0D12, 0.80f), HudArt.Rgba.Hex(0xFFFFFF, 0.10f), 1.5f));
        buttonNormal = ToTexture(HudArt.Panel(64, 14, HudArt.Rgba.Hex(0xFFFFFF, 0.08f), HudArt.Rgba.Hex(0xFFFFFF, 0.22f), 1.5f));
        buttonHover = ToTexture(HudArt.Panel(64, 14, HudArt.Rgba.Hex(0xFFFFFF, 0.16f), HudArt.Rgba.Hex(0xFFFFFF, 0.35f), 1.5f));
        buttonActive = ToTexture(HudArt.Panel(64, 14, HudArt.Rgba.Hex(0xFDC700, 0.45f), HudArt.Rgba.Hex(0xFDC700, 0.9f), 1.5f));
        Pill = ToTexture(HudArt.Pill(32, 160));
        Segment = ToTexture(HudArt.Segment(16, 56, 5f));
        Disc = ToTexture(HudArt.Disc(32));
        Glow = ToTexture(HudArt.Glow(128));
        Wheel = ToTexture(HudArt.SteeringWheel(256));
        Pixel = ToTexture(HudArt.Segment(4, 4, 0f));
    }

    static Texture2D ToTexture(HudArt.Img img)
    {
        var t = new Texture2D(img.W, img.H, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        t.LoadRawTextureData(img.ToBytesBottomUp());
        t.Apply(false, true);
        return t;
    }

    static readonly Dictionary<long, GUIStyle> styles = new Dictionary<long, GUIStyle>();
    static readonly System.Text.RegularExpressions.Regex ColorTags =
        new System.Text.RegularExpressions.Regex("</?color[^>]*>");

    // a text style at `size` units. cached, so calling this every frame is fine.
    public static GUIStyle Text(int size, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        int px = Mathf.Max(6, Mathf.RoundToInt(size * K));
        long key = px * 100L + (bold ? 50 : 0) + (int)anchor;
        if (styles.TryGetValue(key, out GUIStyle s)) return s;
        s = new GUIStyle
        {
            fontSize = px,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            alignment = anchor,
            richText = true,
            wordWrap = false,
            clipping = TextClipping.Overflow,
        };
        s.normal.textColor = Color.white;     // the real color comes from GUI.color in Label()
        styles[key] = s;
        return s;
    }

    // text with a soft drop shadow so it reads over a bright sky or a white line.
    public static void Label(Rect unitRect, string text, GUIStyle style, Color color, float alpha = 1f)
    {
        Rect r = R(unitRect.x, unitRect.y, unitRect.width, unitRect.height);
        Color old = GUI.color;
        float shadow = Mathf.Max(1f, 2f * K);
        GUI.color = new Color(0f, 0f, 0f, 0.55f * color.a * alpha);
        // color tags would ignore the black tint and leave a colored ghost, so the shadow has none.
        string plain = text.IndexOf('<') >= 0 ? ColorTags.Replace(text, "") : text;
        GUI.Label(new Rect(r.x + shadow * 0.5f, r.y + shadow, r.width, r.height), plain, style);
        GUI.color = new Color(color.r, color.g, color.b, color.a * alpha);
        GUI.Label(r, text, style);
        GUI.color = old;
    }

    public static void Label(float x, float y, float w, float h, string text, GUIStyle style, Color color, float alpha = 1f) =>
        Label(new Rect(x, y, w, h), text, style, color, alpha);

    static GUIStyle panelStyle, buttonStyle;

    public static void DrawPanel(float x, float y, float w, float h, float alpha = 1f)
    {
        if (panelStyle == null)
            panelStyle = new GUIStyle { border = new RectOffset(PanelBorder, PanelBorder, PanelBorder, PanelBorder) };
        panelStyle.normal.background = Panel;
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Box(R(x, y, w, h), GUIContent.none, panelStyle);
        GUI.color = old;
    }

    public static bool Button(float x, float y, float w, float h, string text, int size = 18)
    {
        if (buttonStyle == null)
            buttonStyle = new GUIStyle { border = new RectOffset(14, 14, 14, 14), alignment = TextAnchor.MiddleCenter };
        buttonStyle.normal.background = buttonNormal;
        buttonStyle.hover.background = buttonHover;
        buttonStyle.active.background = buttonActive;
        buttonStyle.normal.textColor = buttonStyle.hover.textColor = buttonStyle.active.textColor = TextMain;
        buttonStyle.fontSize = Mathf.Max(6, Mathf.RoundToInt(size * K));
        buttonStyle.fontStyle = FontStyle.Bold;
        return GUI.Button(R(x, y, w, h), text, buttonStyle);
    }

    // a texture tinted with a color.
    public static void Tint(Rect unitRect, Texture2D tex, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(R(unitRect.x, unitRect.y, unitRect.width, unitRect.height), tex);
        GUI.color = old;
    }

    // a vertical bar filled from the bottom: fill = 0..1.
    public static void VerticalBar(float x, float y, float w, float h, float fill, Color color)
    {
        Tint(new Rect(x, y, w, h), Pill, Track);
        fill = Mathf.Clamp01(fill);
        if (fill <= 0.001f) return;
        float fh = h * fill;
        Color old = GUI.color;
        GUI.color = color;
        // draw only the bottom part of the pill picture, so the bar keeps its round bottom
        GUI.DrawTextureWithTexCoords(R(x, y + h - fh, w, fh), Pill, new Rect(0f, 0f, 1f, fill));
        GUI.color = old;
    }

    // draws `draw` turned `degrees` clockwise around the point (cx, cy) in units.
    public static void Rotated(float degrees, float cx, float cy, System.Action draw)
    {
        Matrix4x4 before = GUI.matrix;
        GUIUtility.RotateAroundPivot(degrees, P(cx, cy));   // pivot in real pixels
        draw();
        GUI.matrix = before;
    }

    // time as 38.42 or 1:02.37.
    public static string FormatTime(float seconds, int decimals = 2)
    {
        if (seconds < 0f) seconds = 0f;
        string fmt = decimals == 3 ? "00.000" : "00.00";
        if (seconds < 60f) return seconds.ToString(decimals == 3 ? "0.000" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
        int minutes = (int)(seconds / 60f);
        return minutes + ":" + (seconds - minutes * 60f).ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
}
