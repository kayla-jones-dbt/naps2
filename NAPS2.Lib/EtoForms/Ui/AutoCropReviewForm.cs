using Eto.Forms;
using NAPS2.EtoForms.Layout;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// Shows the automatically detected crop area for one page so the user can accept it, adjust it, or skip the page.
/// </summary>
public class AutoCropReviewForm : CropForm
{
    private readonly Button _skip;
    private readonly Button _acceptAll;

    private AutoCropTransform? _detectedCrop;

    public AutoCropReviewForm(Naps2Config config, UiImageList imageList, ThumbnailController thumbnailController,
        ColorScheme colorScheme, IIconProvider iconProvider) :
        base(config, imageList, thumbnailController, colorScheme, iconProvider)
    {
        _skip = C.Button(UiStrings.Skip, () =>
        {
            Result = AutoCropReviewResult.Skipped;
            Close();
        });
        _acceptAll = C.Button(UiStrings.AcceptAll, () =>
        {
            Apply();
            Result = AutoCropReviewResult.AcceptedAll;
            Close();
        });
    }

    public int PageNumber { get; set; }

    public int PageCount { get; set; }

    public AutoCropReviewResult Result { get; private set; } = AutoCropReviewResult.Cancelled;

    protected override void OnPreLoad(EventArgs e)
    {
        base.OnPreLoad(e);
        Title = string.Format(UiStrings.AutoCropReviewTitle, PageNumber, PageCount);
        if (_detectedCrop == null)
        {
            Title += " - " + UiStrings.AutoCropNoEdgesFound;
        }
    }

    // Any existing auto crop is replaced by the reviewed one
    protected override TransformState GetBaseTransformState(UiImage image) =>
        AutoCropHelper.WithoutAutoCrop(image.TransformState);

    protected override void OnImageRendered(IMemoryImage image)
    {
        _detectedCrop = AutoCropDetector.GetAutoCropTransform(image);
    }

    protected override AutoCropTransform? GetInitialCrop() => _detectedCrop;

    protected override bool UseLastCrop => false;

    protected override bool ProducesAutoCrop => true;

    protected override LayoutElement CreateControls() => C.None();

    protected override LayoutElement CreateExtraButtons() => L.Row(base.CreateExtraButtons(), _skip, _acceptAll);

    protected override void Apply()
    {
        base.Apply();
        Result = AutoCropReviewResult.Accepted;
    }
}

public enum AutoCropReviewResult
{
    Cancelled,
    Accepted,
    Skipped,
    AcceptedAll
}
