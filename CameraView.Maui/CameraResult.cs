namespace CameraView.Maui;

public sealed class CameraResult : EventArgs
{
    public CameraResult()
    {
        Success = false;
    }

    public CameraResult(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        Success = true;
        Image = image;
    }

    public byte[] Image { get; set; }
    public bool Success { get; set; }
}
