// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local

namespace NAPS2.Images.Transforms;

/// <summary>
/// A crop that was produced by automatic page edge detection.
///
/// This is rendered exactly like a CropTransform, but is a distinct type so that it can be found and reverted (or
/// replaced by a manual crop) later without affecting other transforms. For that reason it never simplifies with other
/// transforms.
/// </summary>
public record AutoCropTransform : Transform
{
    public AutoCropTransform()
    {
    }

    public AutoCropTransform(int left, int right, int top, int bottom, int originalWidth, int originalHeight)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
    }

    public int Left { get; private set; }
    public int Right { get; private set; }
    public int Top { get; private set; }
    public int Bottom { get; private set; }

    public int OriginalWidth { get; private set; }
    public int OriginalHeight { get; private set; }

    public CropTransform ToCropTransform() =>
        new(Left, Right, Top, Bottom, OriginalWidth, OriginalHeight);

    public override bool IsNull => Left == 0 && Right == 0 && Top == 0 && Bottom == 0;
}
