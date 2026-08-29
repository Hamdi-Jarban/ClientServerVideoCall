using System.IO;
using System.Windows.Media.Imaging;

namespace VideoCall.Client.Media;

public static class FrameCodec
{
    public static BitmapSource BytesToBitmapSource(byte[] encodedBytes)
    {
        if (encodedBytes is null || encodedBytes.Length == 0)
            throw new ArgumentException("Image bytes are empty.", nameof(encodedBytes));

        using var stream = new MemoryStream(encodedBytes, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
