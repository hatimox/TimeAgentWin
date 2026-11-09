using System.Windows;
using System.Windows.Controls;
using static TimeAgent.Ui;

namespace TimeAgent;

/// Hours + description form for one time entry. Stays open - showing TP's
/// error and keeping what was typed - until `save` succeeds (returns null) or
/// the user confirms discarding the time.
public static class LogPrompt
{
    public static void Run(AppStore store, string title, double hours, string description,
                           string? error, Func<double, string, Task<string?>> save)
    {
        var dlg = new Window
        {
            Title = title, Width = 400, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true, Icon = AppIcon,
        };
        var p = new StackPanel { Margin = new Thickness(14) };
        var err = new TextBlock { Foreground = B("#E5484D"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        var info = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        var hrs = new TextBox { Text = hours.ToString("0.00"), Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        var desc = new TextBox { Text = description, Margin = new Thickness(0, 2, 0, 0) };
        var descLabel = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
        var saveBtn = new Button { Content = "Save", MinWidth = 70, IsDefault = true };
        var cancelBtn = new Button { Content = "Cancel", MinWidth = 70, Margin = new Thickness(4, 0, 0, 0) };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        btns.Children.Add(saveBtn); btns.Children.Add(cancelBtn);

        p.Children.Add(err); p.Children.Add(info);
        p.Children.Add(new TextBlock { Text = "Hours" }); p.Children.Add(hrs);
        p.Children.Add(descLabel); p.Children.Add(desc); p.Children.Add(btns);
        dlg.Content = p;

        void Show(string? e)
        {
            err.Text = e == null ? "" : "⚠ " + e;
            err.Visibility = e == null ? Visibility.Collapsed : Visibility.Visible;
            var req = store.Settings.TimeDescriptionRequired;
            info.Text = req ? "Hours and description (required)." : "Hours and description (optional).";
            descLabel.Text = req ? "Description (required)" : "Description";
        }
        Show(error);

        bool done = false;
        saveBtn.Click += async (_, _) =>
        {
            if (!double.TryParse(hrs.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) || h <= 0)
            { Show("Enter hours greater than 0."); return; }
            if (store.Settings.TimeDescriptionRequired && string.IsNullOrWhiteSpace(desc.Text))
            { Show("A description is required."); return; }
            saveBtn.IsEnabled = false; cancelBtn.IsEnabled = false;
            var e = await save(h, desc.Text);
            saveBtn.IsEnabled = true; cancelBtn.IsEnabled = true;
            if (e == null) { done = true; dlg.Close(); }
            else Show(e);
        };
        cancelBtn.Click += (_, _) => dlg.Close();
        dlg.Closing += (_, ev) =>
        {
            if (done) return;
            var r = MessageBox.Show(dlg, $"{hrs.Text} h will not be logged to TargetProcess.", "Discard this time entry?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) ev.Cancel = true;
            else store.NoteStatus($"Not logged: {title}");
        };

        dlg.ShowDialog();
    }
}
