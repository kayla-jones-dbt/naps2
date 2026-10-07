namespace NAPS2.Images;

/// <summary>
/// Detects page edges and crops away the scanner background for a set of images.
/// </summary>
public class AutoCropOperation : OperationBase
{
    public AutoCropOperation()
    {
        AllowCancel = true;
        AllowBackground = true;
    }

    /// <summary>
    /// The number of images where no page edges could be found (which are left unchanged).
    /// </summary>
    public int NotFoundCount { get; private set; }

    /// <summary>
    /// The number of images that were cropped.
    /// </summary>
    public int CroppedCount { get; private set; }

    public bool Start(UiImageList imageList, List<UiImage> images, AutoCropParams autoCropParams)
    {
        ProgressTitle = MiscResources.AutoCropProgress;
        Status = new OperationStatus
        {
            StatusText = MiscResources.AutoCropping,
            MaxProgress = images.Count
        };

        RunAsync(async () =>
        {
            var changedImages = new List<UiImage>();
            var beforeTransforms = new List<TransformState>();
            var afterTransforms = new List<TransformState>();
            var result = await Pipeline.For(images, CancelToken).StepParallel(img =>
            {
                CancelToken.ThrowIfCancellationRequested();
                var before = img.TransformState;
                var change = AutoCropHelper.Detect(img, before, autoCropParams.ThumbnailSize);
                lock (this)
                {
                    Status.CurrentProgress += 1;
                    if (change == null)
                    {
                        NotFoundCount++;
                    }
                    else
                    {
                        CroppedCount++;
                    }
                }
                InvokeStatusChanged();
                if (change == null)
                {
                    return (img, before, after: (TransformState?) null);
                }
                var (after, thumbnail) = change.Value;
                img.ReplaceTransformState(before, after);
                if (thumbnail != null)
                {
                    img.SetThumbnail(thumbnail, after);
                }
                return (img, before, after: (TransformState?) after);
            }).Run(x =>
            {
                if (x.after != null)
                {
                    changedImages.Add(x.img);
                    beforeTransforms.Add(x.before);
                    afterTransforms.Add(x.after);
                }
            });
            if (changedImages.Count > 0)
            {
                imageList.PushUndoElement(
                    new TransformImagesUndoElement(changedImages, beforeTransforms, afterTransforms));
            }
            return result;
        });

        return true;
    }
}
