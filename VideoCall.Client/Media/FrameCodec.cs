using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace VideoCall.Client.Media;

/// <summary>
/// Converts between OpenCvSharp/raw-JPEG frame representations and WPF's
/// BitmapSource, which is what an Image control's Source needs. Kept
/// separate from VideoCaptureService/UdpMediaClient so neither of them
/// needs to know about WPF types.
/// </summary>
public static class FrameCodec
{
    /// <summary>For the local camera preview: encode a live Mat directly to a frozen BitmapSource.</summary>
    public static BitmapSource MatToBitmapSource(Mat mat)
    {
        Cv2.ImEncode(".bmp", mat, out var bytes);
        return JpegOrBmpBytesToBitmapSource(bytes);
    }

    /// <summary>For the remote video: decode a received/reassembled JPEG frame into a displayable image.</summary>
    public static BitmapSource JpegBytesToBitmapSource(byte[] jpegBytes) => JpegOrBmpBytesToBitmapSource(jpegBytes);

    private static BitmapSource JpegOrBmpBytesToBitmapSource(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze(); // freeze so it can be handed across threads safely
        return frame;
    }
}
