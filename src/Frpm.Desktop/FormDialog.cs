using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Frpm.Desktop;

internal sealed class FormDialog : Window
{
    private readonly StackPanel _body;
    private readonly WrapPanel _buttons;
    private readonly TextBlock _error;
    private readonly Button _submit;
    private bool _busy;

    public FormDialog(string title, StackPanel body, Func<Task> submit, string submitLabel = "保存")
    {
        Title = title;
        Width = 620;
        Height = 650;
        MinHeight = 300;
        MinWidth = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = DesktopShell.Icon();
        _body = body;
        _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed, Margin = new Thickness(0, 8) };
        var accent = DesktopApplication.AccentColor;
        var accentBrush = new SolidColorBrush(accent);
        var hoverBrush = new SolidColorBrush(Darken(accent, 0.86));
        _submit = new Button
        {
            Content = submitLabel,
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = accentBrush,
            BorderBrush = accentBrush,
            Foreground = Brushes.White
        };
        _submit.PointerEntered += (_, _) =>
        {
            _submit.Background = hoverBrush;
            _submit.BorderBrush = hoverBrush;
        };
        _submit.PointerExited += (_, _) =>
        {
            _submit.Background = accentBrush;
            _submit.BorderBrush = accentBrush;
        };
        _submit.Click += async (_, _) => await ExecuteAsync(submit);
        var cancel = new Button { Content = "取消", Cursor = new Cursor(StandardCursorType.Hand) };
        cancel.Click += (_, _) => Close(false);
        _buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _buttons.Children.Add(cancel);
        _buttons.Children.Add(_submit);
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), Margin = new Thickness(24) };
        grid.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetRow(_error, 1);
        Grid.SetRow(_buttons, 2);
        grid.Children.Add(_error);
        grid.Children.Add(_buttons);
        Content = grid;
    }

    public Button AddAction(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(8, 0, 0, 0), Cursor = new Cursor(StandardCursorType.Hand) };
        button.Click += async (_, _) => await ExecuteAsync(action);
        _buttons.Children.Insert(Math.Max(0, _buttons.Children.Count - 1), button);
        return button;
    }

    public void ShowError(string message) => _error.Text = message;

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        _body.IsEnabled = false;
        _submit.IsEnabled = false;
        _error.Text = "正在处理…";
        try { await action(); Close(true); }
        catch (OperationCanceledException) { _error.Text = "操作已取消或等待超时。"; }
        catch (Exception ex) { _error.Text = ex.GetBaseException().Message; }
        finally { _busy = false; _body.IsEnabled = true; _submit.IsEnabled = true; }
    }

    public static StackPanel Field(string label, Control input) => new()
    {
        Spacing = 6,
        Children = { new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, input }
    };

    private static Color Darken(Color color, double factor) => Color.FromRgb(
        (byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));
}
