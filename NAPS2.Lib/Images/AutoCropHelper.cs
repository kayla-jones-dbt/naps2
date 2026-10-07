using System.Collections.Immutable;

namespace NAPS2.Images;

/// <summary>
/// Helpers for applying, finding and removing automatic crops (AutoCropTransform) on images.
/// </summary>
internal static class AutoCropHelper
{
    public static bool HasAutoCrop(TransformState state) => state.Transforms.Any(x => x is AutoCropTransform);

    /// <summary>
    /// Gets the auto crop if it's the last transform (i.e. it can be edited in place without affecting others).
    /// </summary>
    public static AutoCropTransform? GetLastAutoCrop(TransformState state) =>
        state.Transforms.LastOrDefault() as AutoCropTransform;

    public static TransformState WithoutAutoCrop(TransformState state) =>
        HasAutoCrop(state)
            ? new TransformState(state.Transforms.Where(x => x is not AutoCropTransform).ToImmutableList())
            : state;

    /// <summary>
    /// Detects page edges for the image. Any existing auto crop is replaced rather than cropping again on top of it.
    /// Returns null if no edges were found.
    /// </summary>
    public static (TransformState state, IMemoryImage? thumbnail)? Detect(UiImage image, TransformState current,
        int? thumbnailSize)
    {
        using var processedImage = image.GetClonedImage();
        return DetectWithBaseState(processedImage, WithoutAutoCrop(current), thumbnailSize);
    }

    /// <summary>
    /// Renders the image with the given base transforms, detects page edges, and returns the base transforms plus the
    /// auto crop (or null if no edges were found).
    /// </summary>
    public static (TransformState state, IMemoryImage? thumbnail)? DetectWithBaseState(
        ProcessedImage processedImage, TransformState baseState, int? thumbnailSize)
    {
        using var baseImage = processedImage.WithTransformState(baseState);
        var rendered = baseImage.Render();
        try
        {
            var transform = AutoCropDetector.GetAutoCropTransform(rendered);
            if (transform == null)
            {
                return null;
            }
            IMemoryImage? thumbnail = null;
            if (thumbnailSize.HasValue)
            {
                rendered = rendered.PerformTransform(transform);
                thumbnail = rendered.PerformTransform(new ThumbnailTransform(thumbnailSize.Value));
            }
            return (baseState.AddOrSimplify(transform), thumbnail);
        }
        finally
        {
            rendered.Dispose();
        }
    }
}
