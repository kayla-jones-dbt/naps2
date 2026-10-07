using Xunit;

namespace NAPS2.Sdk.Tests.Images;

public class AutoCropDetectorTests : ContextualTests
{
    private const int W = 800;
    private const int H = 1040;
    // Allowed error in pixels (the detector intentionally crops a couple of pixels inside an edge)
    private const int TOLERANCE = 8;

    private const byte LID_WHITE = 254;
    private const byte PAPER_WHITE = TestCanvas.PaperWhite;
    private const byte BACKING_GREY = 205;

    [Fact]
    public void PageOnGreyBacking()
    {
        // Like a manga page scanned on a grey backing sheet, with the page running off the top of the scan
        var canvas = new TestCanvas(W, H, BACKING_GREY);
        canvas.FillRect(0, 0, W, 20, LID_WHITE);
        canvas.FillRect(80, 0, 700, 960, PAPER_WHITE);
        canvas.DrawMangaContent(80, 0, 700, 960);

        var result = AutoCropDetector.Detect(canvas.ToImage(ImageContext));

        AssertCrop(80, result.Left, EdgeKind.Edge, result.LeftKind);
        AssertCrop(W - 700, result.Right, EdgeKind.Edge, result.RightKind);
        AssertCrop(0, result.Top, EdgeKind.None, result.TopKind);
        AssertCrop(H - 960, result.Bottom, EdgeKind.Edge, result.BottomKind);
    }

    [Fact]
    public void PageOnGreyBackingOnWhiteLid()
    {
        // The grey backing sheet doesn't fill the scan area, so there's white lid outside it
        var canvas = new TestCanvas(W, H, LID_WHITE);
        canvas.FillRect(30, 0, 760, 1000, BACKING_GREY);
        canvas.FillRect(90, 0, 710, 940, PAPER_WHITE);
        canvas.DrawMangaContent(90, 0, 710, 940);

        var result = AutoCropDetector.Detect(canvas.ToImage(ImageContext));

        AssertCrop(90, result.Left, EdgeKind.Edge, result.LeftKind);
        AssertCrop(W - 710, result.Right, EdgeKind.Edge, result.RightKind);
        AssertCrop(H - 940, result.Bottom, EdgeKind.Edge, result.BottomKind);
    }

    [Fact]
    public void PageWithShadowOnWhiteLid()
    {
        // A page on a white lid where the page edge casts a faint grey shadow
        var canvas = new TestCanvas(W, H, LID_WHITE);
        canvas.FillRect(60, 50, 740, 990, PAPER_WHITE);
        canvas.FillRect(58, 48, 742, 50, 200);
        canvas.FillRect(58, 990, 742, 992, 200);
        canvas.FillRect(58, 48, 60, 992, 200);
        canvas.FillRect(740, 48, 742, 992, 200);
        canvas.DrawMangaContent(60, 50, 740, 990);

        var result = AutoCropDetector.Detect(canvas.ToImage(ImageContext));

        AssertCrop(60, result.Left, EdgeKind.Edge, result.LeftKind);
        AssertCrop(W - 740, result.Right, EdgeKind.Edge, result.RightKind);
        AssertCrop(50, result.Top, EdgeKind.Edge, result.TopKind);
        AssertCrop(H - 990, result.Bottom, EdgeKind.Edge, result.BottomKind);
    }

    [Fact]
    public void InvisibleEdgeIsNotCropped()
    {
        // A page on a white lid with no visible edge. The page margin can't be told apart from the lid, so it
        // shouldn't crop into the margin at the content.
        var canvas = new TestCanvas(W, H, LID_WHITE);
        canvas.DrawMangaContent(60, 50, 740, 990);

        var result = AutoCropDetector.Detect(canvas.ToImage(ImageContext));

        Assert.False(result.AnyEdgeFound);
        Assert.Equal((0, 0, 0, 0), (result.Left, result.Right, result.Top, result.Bottom));
        Assert.Null(AutoCropDetector.GetAutoCropTransform(canvas.ToImage(ImageContext)));
    }

    [Fact]
    public void ColourCoverOnWhiteLid()
    {
        // Part of a cover jacket that runs off the top and left of the scan, with the art bleeding to its edge
        var canvas = new TestCanvas(W, H, LID_WHITE);
        canvas.FillRect(0, 0, 680, 900, 230, 60, 140);
        canvas.FillRect(0, 600, 400, 680, 230, 230, 40);
        canvas.FillRect(450, 100, 680, 500, 60, 50, 110);

        var result = AutoCropDetector.Detect(canvas.ToImage(ImageContext));

        AssertCrop(0, result.Left, EdgeKind.None, result.LeftKind);
        AssertCrop(0, result.Top, EdgeKind.None, result.TopKind);
        Assert.NotEqual(EdgeKind.None, result.RightKind);
        Assert.InRange(result.Right, W - 680, W - 680 + TOLERANCE);
        Assert.NotEqual(EdgeKind.None, result.BottomKind);
        Assert.InRange(result.Bottom, H - 900, H - 900 + TOLERANCE);
    }

    [Fact]
    public void SlightlySkewedPageIsNotCutOff()
    {
        // If the page isn't perfectly straight, the crop should keep the whole page
        var canvas = new TestCanvas(W, H, BACKING_GREY);
        double angle = 0.6 * Math.PI / 180;
        canvas.FillRotatedRect(400, 500, 600, 880, angle, PAPER_WHITE);

        var result = AutoCropDetector.Detect(canvas.ToImage(ImageContext));

        // The rotated page spans roughly x in [96, 704] and y in [57, 943]
        Assert.Equal(EdgeKind.Edge, result.LeftKind);
        Assert.InRange(result.Left, 80, 100);
        Assert.InRange(result.Right, 80, 100);
        Assert.InRange(result.Top, 40, 61);
        Assert.InRange(result.Bottom, 80, 101);
    }

    [Fact]
    public void BlankImage()
    {
        var canvas = new TestCanvas(W, H, LID_WHITE);

        var result = AutoCropDetector.Detect(canvas.ToImage(ImageContext));

        Assert.False(result.AnyEdgeFound);
    }

    [Fact]
    public void LargeImageScalesCrop()
    {
        // Detection runs on a scaled-down copy; the crop must be scaled back up to the original size
        var canvas = new TestCanvas(W * 3, H * 3, BACKING_GREY);
        canvas.FillRect(240, 0, 2100, 2880, PAPER_WHITE);
        canvas.DrawMangaContent(240, 0, 2100, 2880);

        var transform = AutoCropDetector.GetAutoCropTransform(canvas.ToImage(ImageContext));

        Assert.NotNull(transform);
        Assert.Equal(W * 3, transform.OriginalWidth);
        Assert.Equal(H * 3, transform.OriginalHeight);
        Assert.InRange(transform.Left, 240, 240 + TOLERANCE * 3);
        Assert.InRange(transform.Right, W * 3 - 2100, W * 3 - 2100 + TOLERANCE * 3);
        Assert.Equal(0, transform.Top);
        Assert.InRange(transform.Bottom, H * 3 - 2880, H * 3 - 2880 + TOLERANCE * 3);
    }

    private static void AssertCrop(int expected, int actual, EdgeKind expectedKind, EdgeKind actualKind)
    {
        Assert.Equal(expectedKind, actualKind);
        // The detector should never crop outside the edge (leaving background), only slightly inside
        Assert.InRange(actual, expected, expected + TOLERANCE);
    }
}
