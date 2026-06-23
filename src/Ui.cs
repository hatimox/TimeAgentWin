using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TimeAgent;

/// Shared WPF view helpers (color parsing + small flat-control builders) so the
/// tray popover and the tasks window keep a consistent look. The codebase builds
/// UI in code (no XAML), so these are the common primitives.
public static class Ui
{
    private static ImageSource? _appIcon;

    /// The app icon (clock/checkmark) for window title bars + taskbar.
    public static ImageSource AppIcon =>
        _appIcon ??= BitmapFrame.Create(new Uri("pack://application:,,,/TimeAgent;component/appicon.ico", UriKind.Absolute));

    public static SolidColorBrush B(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]); // #abc -> #aabbcc
        byte a = 255;
        if (hex.Length == 8) { a = Convert.ToByte(hex[..2], 16); hex = hex[2..]; }
        byte r = Convert.ToByte(hex[..2], 16), g = Convert.ToByte(hex.Substring(2, 2), 16), bl = Convert.ToByte(hex.Substring(4, 2), 16);
        return new SolidColorBrush(Color.FromArgb(a, r, g, bl));
    }

    /// A flat, rounded, hover-highlighting click target (we avoid Button chrome
    /// so corners/colors are fully under our control).
    public static Border Clickable(UIElement content, Action onClick, string baseBg, string hoverBg, double radius = 8, Thickness? pad = null)
    {
        var baseBrush = B(baseBg);
        var b = new Border { CornerRadius = new CornerRadius(radius), Background = baseBrush, Padding = pad ?? new Thickness(10, 9, 10, 9), Cursor = Cursors.Hand, Child = content };
        b.MouseEnter += (_, _) => b.Background = B(hoverBg);
        b.MouseLeave += (_, _) => b.Background = baseBrush;
        b.MouseLeftButtonUp += (_, _) => onClick();
        return b;
    }

    public static Border Badge(string text, string bg, string fg = "#FFFFFF", double fontSize = 11) => new()
    {
        CornerRadius = new CornerRadius(6),
        Background = B(bg),
        Padding = new Thickness(7, 2, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, Foreground = B(fg), FontSize = fontSize, FontWeight = FontWeights.Bold },
    };

    /// glyph + text on one line (glyph optional).
    public static TextBlock IconText(string glyph, string text, string fg, double fontSize = 13, FontWeight? weight = null)
    {
        var tb = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = fontSize, Foreground = B(fg), FontWeight = weight ?? FontWeights.Normal };
        if (glyph.Length > 0) tb.Inlines.Add(new Run(glyph + "  "));
        tb.Inlines.Add(new Run(text));
        return tb;
    }

    /// Borderless TextBox with a placeholder shown while empty.
    public static Grid Watermark(TextBox tb, string hint, double fontSize = 13)
    {
        tb.BorderThickness = new Thickness(0); tb.Background = Brushes.Transparent;
        var g = new Grid();
        var hintTb = new TextBlock { Text = hint, Foreground = B("#A9A29B"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), FontSize = fontSize, IsHitTestVisible = false };
        void Upd() => hintTb.Visibility = string.IsNullOrEmpty(tb.Text) ? Visibility.Visible : Visibility.Collapsed;
        tb.TextChanged += (_, _) => Upd(); Upd();
        g.Children.Add(tb); g.Children.Add(hintTb);
        return g;
    }
}
