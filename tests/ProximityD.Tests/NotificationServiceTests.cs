using FluentAssertions;
using ProximityD.Services;

namespace ProximityD.Tests;

public class NotificationServiceTests
{
    [Fact]
    public void Show_RaisesNotificationRequestedEvent()
    {
        var service = new NotificationService();
        NotificationRequest? received = null;
        service.NotificationRequested += (_, req) => received = req;

        service.Show("Test Title", "Test Message");

        received.Should().NotBeNull();
        received!.Title.Should().Be("Test Title");
        received.Message.Should().Be("Test Message");
        received.Type.Should().Be(NotificationType.Info);
    }

    [Fact]
    public void Show_WithType_SetsCorrectType()
    {
        var service = new NotificationService();
        NotificationRequest? received = null;
        service.NotificationRequested += (_, req) => received = req;

        service.Show("Warning", "Something happened", NotificationType.Warning);

        received!.Type.Should().Be(NotificationType.Warning);
    }

    [Fact]
    public void Show_NoSubscribers_DoesNotThrow()
    {
        var service = new NotificationService();
        var act = () => service.Show("Title", "Message");
        act.Should().NotThrow();
    }

    [Fact]
    public void Show_MultipleSubscribers_AllReceiveEvent()
    {
        var service = new NotificationService();
        var count = 0;
        service.NotificationRequested += (_, _) => count++;
        service.NotificationRequested += (_, _) => count++;

        service.Show("Title", "Message");

        count.Should().Be(2);
    }

    [Fact]
    public void NotificationRequest_DefaultValues_AreCorrect()
    {
        var req = new NotificationRequest();
        req.Title.Should().BeEmpty();
        req.Message.Should().BeEmpty();
        req.Type.Should().Be(NotificationType.Info);
    }
}
