using System.Windows;

namespace VideoCall.Client.Views;

public partial class IncomingCallWindow : Window
{
    private int _handled;
    public event Action? Accepted;
    public event Action? Rejected;

    public IncomingCallWindow(string caller)
    {
        InitializeComponent();
        CallerText.Text = $"{caller} يتصل بك";
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (Interlocked.Exchange(ref _handled, 1) != 0) return;
        Accepted?.Invoke();
    }

    private void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (Interlocked.Exchange(ref _handled, 1) != 0) return;
        Rejected?.Invoke();
    }
}
