using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using TuckPane.Models;
using XamlSystemBackdrop = Microsoft.UI.Xaml.Media.SystemBackdrop;

namespace TuckPane.Services;

internal static class GlassThemePalette
{
    internal static bool IsSolid(GlassTheme theme) => theme is GlassTheme.SolidLight or GlassTheme.SolidDark;

    internal static bool IsDark(GlassTheme theme) => theme is GlassTheme.Gray or GlassTheme.SolidDark or GlassTheme.FrostedDark;

    internal static Windows.UI.Color SurfaceColor(GlassTheme theme) => theme switch
    {
        GlassTheme.SolidLight => ColorHelper.FromArgb(255, 241, 239, 233),
        GlassTheme.SolidDark => ColorHelper.FromArgb(255, 47, 45, 45),
        GlassTheme.FrostedLight => ColorHelper.FromArgb(255, 245, 245, 243),
        GlassTheme.FrostedDark => ColorHelper.FromArgb(255, 32, 33, 36),
        GlassTheme.Gray => ColorHelper.FromArgb(255, 47, 45, 45),
        _ => ColorHelper.FromArgb(255, 226, 229, 233)
    };

    internal static Windows.UI.Color ForegroundColor(GlassTheme theme) =>
        IsDark(theme) ? ColorHelper.FromArgb(255, 245, 245, 245) : ColorHelper.FromArgb(255, 31, 31, 31);

    internal static (Windows.UI.Color Tint, Windows.UI.Color Luminosity, float TintOpacity, float LuminosityOpacity) Acrylic(GlassTheme theme) => theme switch
    {
        GlassTheme.Gray =>
            (ColorHelper.FromArgb(255, 32, 33, 36), ColorHelper.FromArgb(255, 47, 45, 45), .44f, .18f),
        GlassTheme.FrostedLight =>
            (ColorHelper.FromArgb(255, 245, 246, 248), ColorHelper.FromArgb(255, 226, 229, 233), .72f, .66f),
        GlassTheme.FrostedDark =>
            (ColorHelper.FromArgb(255, 32, 33, 36), ColorHelper.FromArgb(255, 47, 45, 45), .72f, .22f),
        _ =>
            (ColorHelper.FromArgb(255, 245, 246, 248), ColorHelper.FromArgb(255, 226, 229, 233), .18f, .42f)
    };
}

internal sealed class NeutralAcrylicBackdrop : XamlSystemBackdrop
{
    private readonly GlassTheme _theme;
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;

    public NeutralAcrylicBackdrop(GlassTheme theme = GlassTheme.Light) => _theme = theme;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (GlassThemePalette.IsSolid(_theme) || !DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        _configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = GlassThemePalette.IsDark(_theme) ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light
        };
        ApplyPalette();
        if (_controller.AddSystemBackdropTarget(connectedTarget))
        {
            ApplyConfiguration();
        }
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop connectedTarget,
        XamlRoot xamlRoot)
    {
        ApplyConfiguration();
        ApplyPalette();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        try
        {
            base.OnTargetDisconnected(disconnectedTarget);
        }
        finally
        {
            if (_controller is not null)
            {
                _ = _controller.RemoveSystemBackdropTarget(disconnectedTarget);
                _controller.Dispose();
                _controller = null;
            }
            _configuration = null;
        }
    }

    private void ApplyConfiguration()
    {
        if (_controller is null || _configuration is null)
        {
            return;
        }

        _configuration.IsInputActive = true;
        _configuration.Theme = GlassThemePalette.IsDark(_theme) ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    private void ApplyPalette()
    {
        if (_controller is null)
        {
            return;
        }

        var palette = GlassThemePalette.Acrylic(_theme);
        _controller.TintColor = palette.Tint;
        _controller.FallbackColor = GlassThemePalette.SurfaceColor(_theme);
        _controller.TintOpacity = palette.TintOpacity;
        _controller.LuminosityOpacity = palette.LuminosityOpacity;
    }
}
