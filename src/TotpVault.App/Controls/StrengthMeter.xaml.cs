using System.Windows.Controls;
using System.Windows.Media;
using TotpVault.Core.Security;

namespace TotpVault.App.Controls;

public partial class StrengthMeter : UserControl
{
    private static readonly Brush Red = Frozen("#DC2626");
    private static readonly Brush Orange = Frozen("#EA580C");
    private static readonly Brush Amber = Frozen("#CA8A04");
    private static readonly Brush Green = Frozen("#16A34A");

    public StrengthMeter() => InitializeComponent();

    public void SetLevel(PasswordStrengthLevel level)
    {
        var (count, brush, text) = level switch
        {
            PasswordStrengthLevel.VeryWeak => (1, Red, "強度: 非常に弱い"),
            PasswordStrengthLevel.Weak => (1, Orange, "強度: 弱い"),
            PasswordStrengthLevel.Fair => (2, Amber, "強度: 普通"),
            PasswordStrengthLevel.Strong => (3, Green, "強度: 強い"),
            PasswordStrengthLevel.VeryStrong => (4, Green, "強度: 非常に強い"),
            _ => (0, Red, " "),
        };

        var track = (Brush)FindResource("Brush.Border");
        var bars = new[] { Bar1, Bar2, Bar3, Bar4 };
        for (int i = 0; i < bars.Length; i++) bars[i].Background = i < count ? brush : track;
        LabelText.Text = text;
        LabelText.Foreground = count == 0 ? (Brush)FindResource("Brush.TextMuted") : brush;
    }

    private static Brush Frozen(string color)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        b.Freeze();
        return b;
    }
}
