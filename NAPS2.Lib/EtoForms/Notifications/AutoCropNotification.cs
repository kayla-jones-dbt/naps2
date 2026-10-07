namespace NAPS2.EtoForms.Notifications;

public class AutoCropNotification(UiImageList imageList, int croppedCount, int notFoundCount) : NotificationModel
{
    public UiImageList ImageList { get; } = imageList;
    public int CroppedCount { get; } = croppedCount;
    public int NotFoundCount { get; } = notFoundCount;

    public override NotificationView CreateView()
    {
        return new AutoCropNotificationView(this);
    }
}
