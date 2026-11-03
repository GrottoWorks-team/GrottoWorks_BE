namespace TaskExecution.Domain.Support;

public sealed partial class SupportRequest
{
    public void EnsurePublicationAllowed(int? expectedVersion)
    {
        if (expectedVersion is <= 0)
            throw DomainRuleException.Validation("VALIDATION_FAILED", "Version must be positive.");
        if (expectedVersion.HasValue && expectedVersion.Value != Version)
            throw new DomainRuleException("SUPPORT_REQUEST_VERSION_MISMATCH", "The support request changed.", DomainErrorKind.PreconditionFailed);
        if (Status != SupportRequestStatus.Planning)
            throw new DomainRuleException("SUPPORT_REQUEST_INVALID_STATE", "Only a PLANNING support request can be published.");
        if ((SupportType is SupportType.Staffing or SupportType.Mixed) && positions.Count == 0)
            throw DomainRuleException.Validation("SUPPORT_POSITION_REQUIRED", "Staffing support requires at least one position.");
        if ((SupportType is SupportType.Material or SupportType.Mixed) && materials.Count == 0)
            throw DomainRuleException.Validation("SUPPORT_MATERIAL_REQUIRED", "Material support requires at least one material line.");
        if (positions.Any(position => !position.TaskId.HasValue))
            throw DomainRuleException.Validation("SUPPORT_POSITION_TASK_REQUIRED", "Each position must reference a task before publication.");
    }

    public void Publish(int? expectedVersion, DateTimeOffset now)
    {
        EnsurePublicationAllowed(expectedVersion);
        Status = SupportRequestStatus.Published;
        UpdatedAt = now.ToUniversalTime();
        Version++;
    }
}
