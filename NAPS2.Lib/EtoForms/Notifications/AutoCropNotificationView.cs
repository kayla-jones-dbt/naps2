namespace NAPS2.EtoForms.Notifications;

public class AutoCropNotificationView : LinkNotificationView
{
    private readonly AutoCropNotification _model;

    public AutoCropNotificationView(AutoCropNotification model)
        : base(model, GetTitle(model), model.CroppedCount > 0 ? UiStrings.Undo : MiscResources.Close)
    {
        HideTimeout = model.NotFoundCount > 0 ? HIDE_LONG : HIDE_SHORT;
        _model = model;
    }

    private static string GetTitle(AutoCropNotification model)
    {
        var title = string.Format(UiStrings.AutoCroppedPages, model.CroppedCount);
        if (model.NotFoundCount > 0)
        {
            title += " " + string.Format(UiStrings.AutoCropNoEdgesFoundPages, model.NotFoundCount);
        }
        return title;
    }

    protected override void LinkClick()
    {
        if (_model.CroppedCount > 0)
        {
            _model.ImageList.Undo().AssertNoAwait();
        }
        Manager!.Hide(Model);
    }
}
