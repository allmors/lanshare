using System.Windows;

namespace LanShare.Infrastructure;

public sealed class PasswordPromptDialog : Window
{
    private readonly System.Windows.Controls.PasswordBox _passwordBox;

    public PasswordPromptDialog(string title, string prompt)
    {
        Title = title;
        Width = 420;
        Height = 180;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        Background = System.Windows.Media.Brushes.White;

        var root = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var promptText = new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 10)
        };
        System.Windows.Controls.Grid.SetRow(promptText, 0);
        root.Children.Add(promptText);

        _passwordBox = new System.Windows.Controls.PasswordBox
        {
            MinWidth = 320,
            Padding = new Thickness(8, 6, 8, 6)
        };
        System.Windows.Controls.Grid.SetRow(_passwordBox, 1);
        root.Children.Add(_passwordBox);

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = "确定",
            Width = 88,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okButton.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "取消",
            Width = 88,
            IsCancel = true
        };

        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        System.Windows.Controls.Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => _passwordBox.Focus();
    }

    public string Password => _passwordBox.Password.Trim();
}
