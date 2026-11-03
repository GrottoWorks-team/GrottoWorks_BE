namespace TaskExecution.Domain.Scopes;

public sealed class UserStatusProjection
{
    private UserStatusProjection() { }
    public Guid UserId { get; private set; }
    public Guid? ParishId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public long SourceVersion { get; private set; }
    public static UserStatusProjection Create(Guid userId, Guid? parishId, string status, long version)
    {
        var projection = new UserStatusProjection { UserId = userId };
        projection.Apply(parishId, status, version);
        return projection;
    }
    public bool Apply(Guid? parishId, string status, long version)
    {
        if (version <= SourceVersion) return false;
        if (status is not ("ACTIVE" or "LOCKED") || version < 1)
            throw new ArgumentException("Invalid user status snapshot.");
        ParishId = parishId;
        Status = status;
        SourceVersion = version;
        return true;
    }
}
