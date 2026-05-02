namespace ProximityD.Services;

/// <summary>
/// Provides application-level notifications via system tray balloon tips.
/// Decoupled from the WPF layer — fires events that the App.xaml.cs handles.
/// </summary>
public class NotificationService
{
    /// <summary>Raised when a notification should be displayed to the user.</summary>
    public event EventHandler<NotificationRequest>? NotificationRequested;

    /// <summary>
    /// Requests that a notification be shown.
    /// </summary>
    /// <param name="title">Notification title.</param>
    /// <param name="message">Notification body text.</param>
    /// <param name="type">Severity/type of the notification.</param>
    public void Show(string title, string message, NotificationType type = NotificationType.Info)
    {
        NotificationRequested?.Invoke(this, new NotificationRequest
        {
            Title = title,
            Message = message,
            Type = type
        });
    }
}

/// <summary>Encapsulates the data for a notification request.</summary>
public class NotificationRequest
{
    /// <summary>Gets or sets the notification title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the notification message body.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the notification type/severity.</summary>
    public NotificationType Type { get; set; } = NotificationType.Info;
}

/// <summary>Classification of notification severity.</summary>
public enum NotificationType
{
    /// <summary>General informational notification.</summary>
    Info,
    /// <summary>Warning that requires user attention.</summary>
    Warning,
    /// <summary>Error that needs user action.</summary>
    Error
}
