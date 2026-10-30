using BuildingBlocks.Security;
using NotificationAudit.Contracts.Notifications;
using NotificationAudit.Domain.Notifications;

namespace NotificationAudit.Application.Notifications;

public interface INotificationPreferenceRepository
{
    Task<NotificationPreference?> GetAsync(Guid userId, bool tracking, CancellationToken cancellationToken);
    void Add(NotificationPreference preference);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class GetNotificationPreferenceHandler(
    ICurrentUser currentUser,
    INotificationPreferenceRepository repository)
{
    public async Task<NotificationPreferenceDto> HandleAsync(CancellationToken cancellationToken)
    {
        var preference = await repository.GetAsync(currentUser.UserId, false, cancellationToken);
        return preference is null
            ? new NotificationPreferenceDto(true, true, false)
            : Map(preference);
    }

    internal static NotificationPreferenceDto Map(NotificationPreference preference) =>
        new(preference.InAppEnabled, preference.PushEnabled, preference.EmailEnabled);
}

public sealed class UpdateNotificationPreferenceHandler(
    ICurrentUser currentUser,
    INotificationPreferenceRepository repository,
    TimeProvider timeProvider)
{
    public async Task<NotificationPreferenceDto> HandleAsync(
        UpdateNotificationPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        var preference = await repository.GetAsync(currentUser.UserId, true, cancellationToken);
        if (preference is null)
        {
            preference = NotificationPreference.CreateDefault(currentUser.UserId, timeProvider.GetUtcNow());
            repository.Add(preference);
        }

        preference.Update(
            request.InAppEnabled!.Value,
            request.PushEnabled!.Value,
            request.EmailEnabled!.Value,
            timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return GetNotificationPreferenceHandler.Map(preference);
    }
}
