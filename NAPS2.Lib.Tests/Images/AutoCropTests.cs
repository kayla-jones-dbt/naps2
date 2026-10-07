using NAPS2.Sdk.Tests;
using NAPS2.Sdk.Tests.Images;
using Xunit;

namespace NAPS2.Lib.Tests.Images;

public class AutoCropTests : ContextualTests
{
    private const byte BACKING_GREY = 205;
    private const byte LID_WHITE = 254;

    [Fact]
    public async Task AutoCrop_CropsAndCanBeUndone()
    {
        var list = new UiImageList();
        var image = CreatePageOnGreyBacking();
        list.Mutate(new ListMutation<UiImage>.Append(image));

        var op = await RunAutoCrop(list, image);

        Assert.Equal(1, op.CroppedCount);
        Assert.Equal(0, op.NotFoundCount);
        var autoCrop = Assert.IsType<AutoCropTransform>(Assert.Single(image.TransformState.Transforms));
        Assert.InRange(autoCrop.Left, 80, 90);

        await list.Undo();
        Assert.True(image.TransformState.IsEmpty);
    }

    [Fact]
    public async Task AutoCrop_ReplacesExistingAutoCrop()
    {
        var list = new UiImageList();
        var image = CreatePageOnGreyBacking();
        list.Mutate(new ListMutation<UiImage>.Append(image));

        await RunAutoCrop(list, image);
        var first = image.TransformState;
        await RunAutoCrop(list, image);

        // Running again shouldn't crop on top of the previous crop
        Assert.Equal(first, image.TransformState);
    }

    [Fact]
    public async Task AutoCrop_NoEdgesFound_LeavesImageUnchanged()
    {
        var list = new UiImageList();
        var canvas = new TestCanvas(400, 520, LID_WHITE);
        var image = new UiImage(ScanningContext.CreateProcessedImage(canvas.ToImage(ImageContext)));
        list.Mutate(new ListMutation<UiImage>.Append(image));

        var op = await RunAutoCrop(list, image);

        Assert.Equal(0, op.CroppedCount);
        Assert.Equal(1, op.NotFoundCount);
        Assert.True(image.TransformState.IsEmpty);
        Assert.False(list.CanUndo);
    }

    [Fact]
    public async Task RevertAutoCrop_KeepsOtherTransforms()
    {
        var list = new UiImageList();
        var image = CreatePageOnGreyBacking();
        list.Mutate(new ListMutation<UiImage>.Append(image));
        list.Mutate(new ImageListMutation.AddTransforms([new BrightnessTransform(10)]), ListSelection.Of(image));
        await RunAutoCrop(list, image);
        list.Mutate(new ImageListMutation.AddTransforms([new RotationTransform(90)]), ListSelection.Of(image));
        Assert.Equal(3, image.TransformState.Transforms.Count);

        list.Mutate(new ImageListMutation.RevertAutoCrop(), ListSelection.Of(image));

        Assert.Equal([new BrightnessTransform(10), new RotationTransform(90)], image.TransformState.Transforms);

        // Reverting can be undone
        await list.Undo();
        Assert.Contains(image.TransformState.Transforms, x => x is AutoCropTransform);
    }

    [Fact]
    public async Task Deskew_RedoesAutoCropAfterRotation()
    {
        var list = new UiImageList();
        var image = CreateSkewedPageOnGreyBacking();
        list.Mutate(new ListMutation<UiImage>.Append(image));
        await RunAutoCrop(list, image);
        Assert.IsType<AutoCropTransform>(Assert.Single(image.TransformState.Transforms));

        var op = new DeskewOperation();
        op.Start(list, [image], new DeskewParams());
        await op.Success!;

        // The page is rotated first and then auto cropped, rather than rotating the already cropped page
        Assert.Equal(2, image.TransformState.Transforms.Count);
        var rotation = Assert.IsType<RotationTransform>(image.TransformState.Transforms[0]);
        // Angles are normalized to [0, 360)
        double angle = rotation.Angle > 180 ? rotation.Angle - 360 : rotation.Angle;
        Assert.InRange(Math.Abs(angle), 2, 4);
        Assert.IsType<AutoCropTransform>(image.TransformState.Transforms[1]);

        // Undo restores the previous auto crop
        await list.Undo();
        Assert.IsType<AutoCropTransform>(Assert.Single(image.TransformState.Transforms));
    }

    [Fact]
    public void GetBaseTransformState_RemovesOnlyAutoCrops()
    {
        var state = TransformState.Empty
            .AddOrSimplify(new BrightnessTransform(10))
            .AddOrSimplify(new AutoCropTransform(1, 2, 3, 4, 100, 100))
            .AddOrSimplify(new RotationTransform(90));

        Assert.True(AutoCropHelper.HasAutoCrop(state));
        Assert.Null(AutoCropHelper.GetLastAutoCrop(state));
        Assert.Equal([new BrightnessTransform(10), new RotationTransform(90)],
            AutoCropHelper.WithoutAutoCrop(state).Transforms);
    }

    private static async Task<AutoCropOperation> RunAutoCrop(UiImageList list, UiImage image)
    {
        var op = new AutoCropOperation();
        op.Start(list, [image], new AutoCropParams());
        await op.Success!;
        return op;
    }

    private UiImage CreatePageOnGreyBacking()
    {
        var canvas = new TestCanvas(800, 1040, BACKING_GREY);
        canvas.FillRect(80, 0, 700, 960, TestCanvas.PaperWhite);
        canvas.DrawMangaContent(80, 0, 700, 960);
        return new UiImage(ScanningContext.CreateProcessedImage(canvas.ToImage(ImageContext)));
    }

    private UiImage CreateSkewedPageOnGreyBacking()
    {
        // A page with lines of "text", rotated by 3 degrees
        var canvas = new TestCanvas(800, 1040, BACKING_GREY);
        double angle = 3 * Math.PI / 180;
        canvas.FillRotatedRect(400, 520, 560, 820, angle, TestCanvas.PaperWhite);
        for (int line = 0; line < 30; line++)
        {
            // Text line centres along the rotated page's vertical axis
            double offset = -360 + line * 24;
            double cx = 400 - offset * Math.Sin(angle), cy = 520 + offset * Math.Cos(angle);
            canvas.FillRotatedRect(cx, cy, 440, 6, angle, 0);
        }
        return new UiImage(ScanningContext.CreateProcessedImage(canvas.ToImage(ImageContext)));
    }
}
