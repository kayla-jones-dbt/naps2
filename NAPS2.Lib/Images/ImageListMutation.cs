namespace NAPS2.Images;

public abstract class ImageListMutation : ListMutation<UiImage>
{
    public class RotateFlip(double angle) : ImageListMutation
    {
        public override void Apply(List<UiImage> list, ref ListSelection<UiImage> selection)
        {
            foreach (UiImage img in selection)
            {
                var transform = new RotationTransform(angle);
                var thumb = img.GetThumbnailClone();
                var updatedThumb = thumb?.PerformTransform(transform);
                img.AddTransform(transform, updatedThumb);
            }
        }
    }

    public class AddTransforms(List<Transform> transforms, Dictionary<UiImage, IMemoryImage?>? updatedThumbnails = null)
        : ListMutation<UiImage>
    {
        public override void Apply(List<UiImage> list, ref ListSelection<UiImage> selection)
        {
            if (transforms.Any(x => !x.IsNull))
            {
                foreach (UiImage img in selection)
                {
                    img.AddTransforms(transforms, updatedThumbnails?.Get(img));
                }
            }
        }
    }

    /// <summary>
    /// Removes automatic crops from the selected images, leaving any other transforms.
    /// </summary>
    public class RevertAutoCrop : ListMutation<UiImage>
    {
        public override void Apply(List<UiImage> list, ref ListSelection<UiImage> selection)
        {
            foreach (UiImage img in selection)
            {
                var state = img.TransformState;
                if (AutoCropHelper.HasAutoCrop(state))
                {
                    img.ReplaceTransformState(state, AutoCropHelper.WithoutAutoCrop(state));
                }
            }
        }
    }

    /// <summary>
    /// Replaces the selected images' transforms (if unchanged) with the corresponding new transforms.
    /// </summary>
    public class ReplaceTransformStates(
        Dictionary<UiImage, (TransformState before, TransformState after)> changes,
        Dictionary<UiImage, IMemoryImage?>? updatedThumbnails = null) : ListMutation<UiImage>
    {
        public override void Apply(List<UiImage> list, ref ListSelection<UiImage> selection)
        {
            foreach (UiImage img in selection)
            {
                if (changes.TryGetValue(img, out var change))
                {
                    img.ReplaceTransformState(change.before, change.after);
                    if (updatedThumbnails?.Get(img) is { } thumbnail)
                    {
                        if (img.TransformState == change.after)
                        {
                            img.SetThumbnail(thumbnail, change.after);
                        }
                        else
                        {
                            thumbnail.Dispose();
                        }
                    }
                }
            }
        }
    }

    public class ResetTransforms : ListMutation<UiImage>
    {
        public override void Apply(List<UiImage> list, ref ListSelection<UiImage> selection)
        {
            foreach (UiImage img in selection)
            {
                img.ResetTransforms();
            }
        }
    }
}