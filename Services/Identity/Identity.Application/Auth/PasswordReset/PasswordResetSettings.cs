namespace Identity.Application.Auth.PasswordReset;

/// <summary>Bound from configuration section <c>PasswordReset</c> in Infrastructure.</summary>
public sealed class PasswordResetSettings
{
    public const string SectionName = "PasswordReset";

    /// <summary>Reset codes are short-lived (default 30 minutes).</summary>
    public int TokenMinutes { get; init; } = 30;

    /// <summary>DEV ONLY: write the reset code to the log because no email channel exists yet.</summary>
    public bool ExposeTokenInLogs { get; init; }
}
