using System.Windows;
using VideoCall.Shared.Messages;
using VideoCall.Client.Services;

namespace VideoCall.Client.Views;

public partial class RoomInviteWindow : Window
{
    private readonly NetworkClient _network;
    private readonly RoomInvitePayload _invite;
    private int _handled;

    public RoomInviteWindow(NetworkClient network, RoomInvitePayload invite)
    {
        InitializeComponent();
        _network = network;
        _invite = invite;
        InviteText.Text = $"دعاك {invite.Host} للانضمام إلى الغرفة: {invite.RoomId}";
    }

    private async void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (Interlocked.Exchange(ref _handled, 1) != 0) return;
        try
        {
            await _network.AcceptRoomInviteAsync(_invite);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر قبول الدعوة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            Interlocked.Exchange(ref _handled, 0);
        }
    }

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (Interlocked.Exchange(ref _handled, 1) != 0) return;
        try
        {
            await _network.RejectRoomInviteAsync(_invite);
        }
        finally
        {
            DialogResult = false;
        }
    }
}
