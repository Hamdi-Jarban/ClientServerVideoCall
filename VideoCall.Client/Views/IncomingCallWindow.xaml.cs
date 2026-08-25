using System.Windows;

namespace VideoCall.Client.Views;

public partial class IncomingCallWindow : Window
{
    public event Action? Accepted;
    public event Action? Rejected;

    public IncomingCallWindow(string callerName)
    {
        InitializeComponent();
        CallerText.Text = $"{callerName} يتصل بك...";
    }

    private void Accept_Click(object sender, RoutedEventArgs e) => Accepted?.Invoke();

    private void Reject_Click(object sender, RoutedEventArgs e) => Rejected?.Invoke();
}
