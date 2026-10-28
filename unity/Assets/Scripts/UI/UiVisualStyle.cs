using UnityEngine;

namespace Inspection.UI
{
    /// <summary>巡检端共享的视觉语义：浅色工作面、深靛蓝主操作，颜色始终配合中文文字。</summary>
    public static class UiVisualStyle
    {
        public const int BodyFontSize = 42;
        public const int ButtonFontSize = 48;
        public const float MinimumButtonHeight = 144f;
        public const float MinimumInputHeight = 132f;

        public static readonly Color Page = FromHex("F1F5F9");
        public static readonly Color Surface = Color.white;
        public static readonly Color Ink = FromHex("172033");
        public static readonly Color Secondary = FromHex("53657A");
        public static readonly Color Border = FromHex("DCE3EC");
        public static readonly Color Primary = FromHex("263B80");
        public static readonly Color PrimaryPressed = FromHex("1C2C63");
        public static readonly Color Disabled = FromHex("A5B0C0");
        public static readonly Color Error = FromHex("B42318");
        public static readonly Color Low = FromHex("247A4B");
        public static readonly Color Medium = FromHex("B35C00");
        public static readonly Color High = FromHex("C0393B");
        public static readonly Color Open = FromHex("B5472C");
        public static readonly Color InProgress = FromHex("2866A8");
        public static readonly Color Resolved = FromHex("247A4B");
        public static readonly Color TopBar = new Color(0.055f, 0.075f, 0.12f, 0.90f);

        public static Color FromHex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var color);
            return color;
        }

        public static Color Tint(Color foreground)
        {
            return new Color(foreground.r, foreground.g, foreground.b, 0.10f);
        }

        public static Color Priority(string priority)
        {
            switch (priority)
            {
                case "low": return Low;
                case "high": return High;
                default: return Medium;
            }
        }

        public static Color Status(string status)
        {
            switch (status)
            {
                case "in_progress": return InProgress;
                case "resolved": return Resolved;
                default: return Open;
            }
        }
    }
}
