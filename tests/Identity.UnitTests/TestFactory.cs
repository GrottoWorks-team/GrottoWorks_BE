namespace Identity.UnitTests;

public sealed record TestStores(
    FakeUserStore Users,
    FakeRefreshTokenStore RefreshTokens,
    FakeSkillStore Skills,
    FakeIntegrationEventPublisher Events);

/// <summary>Shared wiring to build handler instances without a database.</summary>
public static class TestFactory
{
    public static TestStores CreateEmptyStores() =>
        new(new FakeUserStore(), new FakeRefreshTokenStore(), new FakeSkillStore(), new FakeIntegrationEventPublisher());
}