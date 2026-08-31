using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TuckPane.Models;
using Windows.Graphics;
using WinRT.Interop;

namespace TuckPane.Services;

internal sealed class OwnedDialogWindow : Window
{
    private readonly IntPtr _owner;
    private readonly Grid _root;
    private readonly Button _primaryButton;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<bool>? _tryAccept;
    private bool _accepted;
    private bool _ownerDisabled;

    private OwnedDialogWindow(
        IntPtr owner,
        DisplayInfo display,
        GlassTheme theme,
        string title,
        FrameworkElement body,
        string primaryText,
        string cancelText)
    {
        _owner = owner;
        Title = title;

        _root = new Grid
        {
            Padding = new Thickness(24, 20, 24, 20),
            RowSpacing = 18,
            Background = new SolidColorBrush(GlassThemePalette.SurfaceColor(theme)),
            RequestedTheme = GlassThemePalette.IsDark(theme) ? ElementTheme.Dark : ElementTheme.Light
        };
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.KeyDown += Root_KeyDown;

        Grid.SetRow(body, 0);
        _root.Children.Add(body);

        _primaryButton = new Button
        {
            Content = primaryText,
            MinWidth = 92,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _primaryButton.Click += (_, _) => Accept();
        var cancelButton = new Button { Content = cancelText, MinWidth = 92 };
        cancelButton.Click += (_, _) => Close();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.Children.Add(_primaryButton);
        actions.Children.Add(cancelButton);
        Grid.SetRow(actions, 1);
        _root.Children.Add(actions);
        Content = _root;

        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        AppWindow appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.IsShownInSwitchers = false;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, true);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        NativeMethods.RECT bounds = DisplayPlacementService.CalculateCenteredDialogBounds(display);
        appWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        if (owner != IntPtr.Zero)
        {
            _ = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT, owner);
            long extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            extendedStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            extendedStyle &= ~NativeMethods.WS_EX_APPWINDOW;
            _ = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(extendedStyle));
        }

        Closed += OwnedDialogWindow_Closed;
    }

    internal static Task<bool> ShowTextInputAsync(
        IntPtr owner,
        DisplayInfo display,
        GlassTheme theme,
        string title,
        string defaultText,
        string primaryText,
        string cancelText,
        Func<string, string?> validateAndAccept)
    {
        var input = new TextBox { Text = defaultText, MaxLength = 120 };
        var error = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.IndianRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var body = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(input);
        body.Children.Add(error);

        var window = new OwnedDialogWindow(owner, display, theme, title, body, primaryText, cancelText);
        window._tryAccept = () =>
        {
            string? validationError = validateAndAccept(input.Text);
            if (validationError is null) return true;
            error.Text = validationError;
            error.Visibility = Visibility.Visible;
            input.SelectAll();
            _ = input.Focus(FocusState.Programmatic);
            return false;
        };
        window._root.Loaded += (_, _) =>
        {
            input.SelectAll();
            _ = input.Focus(FocusState.Programmatic);
        };
        return window.ShowAsync();
    }

    internal static Task<bool> ShowConfirmationAsync(
        IntPtr owner,
        DisplayInfo display,
        GlassTheme theme,
        string title,
        string message,
        string primaryText,
        string cancelText)
    {
        var body = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var window = new OwnedDialogWindow(owner, display, theme, title, body, primaryText, cancelText)
        {
            _tryAccept = static () => true
        };
        window._root.Loaded += (_, _) => _ = window._primaryButton.Focus(FocusState.Programmatic);
        return window.ShowAsync();
    }

    private Task<bool> ShowAsync()
    {
        try
        {
            if (_owner != IntPtr.Zero)
            {
                _ownerDisabled = NativeMethods.IsWindowEnabled(_owner);
                if (_ownerDisabled) _ = NativeMethods.EnableWindow(_owner, false);
            }
            Activate();
            return _completion.Task;
        }
        catch
        {
            RestoreOwner();
            throw;
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            Accept();
        }
    }

    private void Accept()
    {
        if (_tryAccept is null || !_tryAccept()) return;
        _accepted = true;
        Close();
    }

    private void OwnedDialogWindow_Closed(object sender, WindowEventArgs args)
    {
        RestoreOwner();
        _completion.TrySetResult(_accepted);
    }

    private void RestoreOwner()
    {
        if (!_ownerDisabled) return;
        _ownerDisabled = false;
        _ = NativeMethods.EnableWindow(_owner, true);
    }
}
