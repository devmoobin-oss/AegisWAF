using System.Windows;

namespace AegisWAF.Desktop;

/// <summary>Minimal secret prompt (used for administrator password resets).</summary>
public partial class PromptWindow : Window
{
    public string Value => Box.Password;

    public PromptWindow(Window owner, string title, string message)
    {
        InitializeComponent();
        Owner = owner; Title = title; Message.Text = message;
        Loaded += (_, _) => Box.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
