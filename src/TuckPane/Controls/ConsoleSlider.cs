using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TuckPane.Controls;

public sealed class ConsoleSlider : Slider
{
    private Thumb? _thumb;
    private SolidColorBrush _normalBrush = new(Windows.UI.Color.FromArgb(255, 250, 249, 246));
    private SolidColorBrush _borderBrush = new(Windows.UI.Color.FromArgb(255, 184, 183, 179));

    public ConsoleSlider() => IsEnabledChanged += (_, _) => UpdateThumbOpacity();

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _thumb = GetTemplateChild("HorizontalThumb") as Thumb;
        if (_thumb is null) return;
        _thumb.Width = 8;
        _thumb.Height = 22;
        _thumb.Template = (ControlTemplate)Application.Current.Resources["ConsoleSliderThumbTemplate"];
        _thumb.Background = _normalBrush;
        _thumb.BorderBrush = _borderBrush;
        _thumb.BorderThickness = new Thickness(1);
        _thumb.PointerEntered += (_, _) => _thumb.Width = 9;
        _thumb.PointerExited += (_, _) => _thumb.Width = 8;
        _thumb.PointerPressed += (_, _) => _thumb.Opacity = .86;
        _thumb.PointerReleased += (_, _) => UpdateThumbOpacity();
        UpdateThumbOpacity();
    }

    public void SetThumbPalette(Windows.UI.Color color, Windows.UI.Color borderColor)
    {
        _normalBrush = new SolidColorBrush(color);
        _borderBrush = new SolidColorBrush(borderColor);
        if (_thumb is null) return;
        _thumb.Background = _normalBrush;
        _thumb.BorderBrush = _borderBrush;
        _thumb.BorderThickness = new Thickness(1);
    }

    private void UpdateThumbOpacity()
    {
        if (_thumb is not null) _thumb.Opacity = IsEnabled ? 1 : .45;
    }
}
