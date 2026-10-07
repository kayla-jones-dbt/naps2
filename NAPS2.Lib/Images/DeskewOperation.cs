namespace NAPS2.Images;

public class DeskewOperation : OperationBase
{
    public DeskewOperation()
    {
        AllowCancel = true;
        AllowBackground = true;
    }

    public bool Start(UiImageList imageList, List<UiImage> images, DeskewParams deskewParams)
    {
        ProgressTitle = MiscResources.AutoDeskewProgress;
        Status = new OperationStatus
        {
            StatusText = MiscResources.AutoDeskewing,
            MaxProgress = images.Count
        };

        RunAsync(async () =>
        {
            var beforeTransforms = new List<TransformState>();
            var afterTransforms = new List<TransformState>();
            var result = await Pipeline.For(images, CancelToken).StepParallel(img =>
            {
                using var processedImage = img.GetClonedImage();
                var image = processedImage.Render();
                try
                {
                    CancelToken.ThrowIfCancellationRequested();
                    var transform = Deskewer.GetDeskewTransform(image);
                    CancelToken.ThrowIfCancellationRequested();
                    if (AutoCropHelper.HasAutoCrop(img.TransformState) && !transform.IsNull)
                    {
                        // Auto cropping must happen after deskewing (as the page edges are only straight once the
                        // page is deskewed), so remove the existing auto crop, deskew, and then auto crop again.
                        var beforeState = img.TransformState;
                        var deskewedState = AutoCropHelper.WithoutAutoCrop(beforeState).AddOrSimplify(transform);
                        var recropped = AutoCropHelper.DetectWithBaseState(processedImage, deskewedState,
                            deskewParams.ThumbnailSize);
                        var afterState = recropped?.state ?? deskewedState;
                        img.ReplaceTransformState(beforeState, afterState);
                        if (recropped?.thumbnail is { } recroppedThumbnail)
                        {
                            img.SetThumbnail(recroppedThumbnail, afterState);
                        }
                        lock (this)
                        {
                            Status.CurrentProgress += 1;
                        }
                        InvokeStatusChanged();
                        return (before: beforeState, after: afterState);
                    }
                    image = image.PerformTransform(transform);
                    var thumbnail = deskewParams.ThumbnailSize.HasValue
                        ? image.PerformTransform(new ThumbnailTransform(deskewParams.ThumbnailSize.Value))
                        : null;
                    var before = img.TransformState;
                    img.AddTransform(transform, thumbnail);
                    var after = img.TransformState;
                    lock (this)
                    {
                        Status.CurrentProgress += 1;
                    }
                    InvokeStatusChanged();
                    return (before, after);
                }
                finally
                {
                    image.Dispose();
                }
            }).Run(transformState =>
            {
                beforeTransforms.Add(transformState.before);
                afterTransforms.Add(transformState.after);
            });
            imageList.PushUndoElement(new TransformImagesUndoElement(images, beforeTransforms, afterTransforms));
            return result;
        });

        return true;
    }
}