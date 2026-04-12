using System;
using System.Windows;
using System.Windows.Media;

namespace CadSyncPlugin
{
    /// <summary>
    /// Gestiona el tema visual del plugin (oscuro / claro) usando los mismos
    /// tokens de color que el design system de la aplicación web (index.css).
    /// Actualiza los DynamicResource del FrameworkElement raíz para que todos
    /// los controles que usen {DynamicResource ...} reaccionen al cambio.
    /// </summary>
    public static class ThemeManager
    {
        // ── Persistence key ──────────────────────────────────
        private const string PrefKey = "CadSyncTheme";
        public static bool IsDark { get; private set; } = true;

        /// <summary>Fired after any theme change so external code can react.</summary>
        public static event Action? ThemeChanged;

        // ── Public API ────────────────────────────────────────
        public static void LoadSaved(FrameworkElement root)
        {
            string saved = "";
            try { saved = System.IO.File.ReadAllText(PrefPath()).Trim(); } catch { }
            IsDark = saved != "light";
            Apply(root);
        }

        public static void Toggle(FrameworkElement root)
        {
            IsDark = !IsDark;
            Apply(root);
            try { System.IO.File.WriteAllText(PrefPath(), IsDark ? "dark" : "light"); } catch { }
            ThemeChanged?.Invoke();
        }

        private static string PrefPath() =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CadSyncTheme.txt");

        // ── Apply palette to a root element's ResourceDictionary ─
        public static void Apply(FrameworkElement root)
        {
            var d = root.Resources;

            // ── Backgrounds ──
            Set(d, "BgMain",      IsDark ? "#1B1D23" : "#F0F2F5");
            Set(d, "BgPanel",     IsDark ? "#21252B" : "#FFFFFF");
            Set(d, "BgTertiary",  IsDark ? "#272C36" : "#E8EBF0");
            Set(d, "BgDeep",      IsDark ? "#14161b" : "#E2E5EA");

            // ── Text ──
            Set(d, "TextMain",       IsDark ? "#D2D2D2" : "#1B1D23");
            Set(d, "TextSecondary",  IsDark ? "#8A91A1" : "#5A6170");
            Set(d, "TextHint",       IsDark ? "#4e6a85" : "#8A91A1");
            Set(d, "TextDisabled",   IsDark ? "#5A6170" : "#9AA0AD");

            // ── Borders ──
            Set(d, "BorderColor",    IsDark ? "#343B48" : "#DDE1E8");
            Set(d, "BorderSubtle",   IsDark ? "#2a303a" : "#EAEDF1");
            Set(d, "BorderFocus",    IsDark ? "#5B657C" : "#B0B8C8");

            // ── Status backgrounds ──
            Set(d, "SuccessBg",   IsDark ? "#0d200d" : "#E8F5E9");
            Set(d, "WarningBg",   IsDark ? "#1f1a00" : "#FFF8E1");
            Set(d, "ErrorBg",     IsDark ? "#200808" : "#FFEBEE");
            Set(d, "AccentSubtle",IsDark ? "#0e1e31" : "#EBF4FF");
            Set(d, "SuccessBorder", IsDark ? "#1f4d1f" : "#A5D6A7");
            Set(d, "WarningBorder", IsDark ? "#4d3d00" : "#FFE082");
            Set(d, "ErrorBorder",   IsDark ? "#4d1010" : "#EF9A9A");

            // ── Constants (same in both themes) ──
            Set(d, "AccentColor",    "#55AAFF");
            Set(d, "AccentHover",    "#4aa0ec");
            Set(d, "SuccessColor",   "#4CAF50");
            Set(d, "WarningColor",   "#FFC107");
            Set(d, "ErrorColor",     "#F44336");
            Set(d, "InfoColor",      "#2196F3");

            // ── Accent gradient (for logo badge) ──
            d["LogoGradient"] = new LinearGradientBrush(
                Color.FromRgb(0x55, 0xAA, 0xFF),
                Color.FromRgb(0x3e, 0x8e, 0xd0),
                new Point(0, 0), new Point(1, 1));
        }

        // ── Helper ────────────────────────────────────────────
        private static void Set(ResourceDictionary d, string key, string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);

            // If the key already exists as a SolidColorBrush, mutate in place so
            // WPF's DynamicResource binding picks it up without re-allocation.
            if (d.Contains(key) && d[key] is SolidColorBrush existing)
            {
                existing.Color = color;
            }
            else
            {
                d[key] = new SolidColorBrush(color);
            }
        }
    }
}
