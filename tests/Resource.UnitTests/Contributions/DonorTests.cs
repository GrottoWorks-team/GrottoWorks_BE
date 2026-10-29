using BuildingBlocks.Security;
using Resource.Application.Contributions;
using Resource.Domain;

namespace Resource.UnitTests.Contributions;

/// <summary>F-RES-12: donors — parish scope, no automatic merge, optional account link.</summary>
public sealed class DonorTests
{
    private static readonly Guid Parish = Guid.NewGuid();
    private static readonly Guid OtherParish = Guid.NewGuid();
    private readonly FakeDonorStore _store = new();
    private readonly FakeIdentityUserLookup _identity = new();

    private static FakeCurrentUser Officer(Guid? parish = null) => new(Guid.NewGuid(), GrottoWorksRoles.MaterialOfficer, parish ?? Parish);

    private static FakeCurrentUser Admin() => new(Guid.NewGuid(), GrottoWorksRoles.Admin);

    private CreateDonorCommandHandler Create(FakeCurrentUser? user = null)
    {
        var current = user ?? Officer();
        return new(new DonorAccess(_store, current), _store, _identity, current);
    }

    private UpdateDonorCommandHandler Update(FakeCurrentUser? user = null) => new(new DonorAccess(_store, user ?? Officer()), _store);

    private ListDonorsQueryHandler List(FakeCurrentUser? user = null) => new(new DonorAccess(_store, user ?? Officer()), _store);

    private static CreateDonorCommand Donor(string name = "Bà Maria Lan", Guid? userId = null, Guid? parishId = null, string? phone = "0901 234 567", string? email = null) =>
        new(parishId, userId, name, phone, email, null, null);

    private static async Task<DomainRuleException> Fails(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainRuleException>()).Which;

    [Fact]
    public async Task Officer_creates_an_external_donor_in_their_parish()
    {
        var dto = await Create().HandleAsync(new CreateDonorCommand(null, null, "  Bà Maria Lan ", " 0901 234 567 ", " Lan@Example.com ", "", "Tặng đèn hằng năm"));

        dto.ParishId.Should().Be(Parish);
        dto.UserId.Should().BeNull();
        dto.DonorName.Should().Be("Bà Maria Lan");
        dto.Phone.Should().Be("0901 234 567");
        dto.Email.Should().Be("lan@example.com");
        dto.Address.Should().BeNull();
        _store.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Same_name_and_phone_create_two_donors_never_merged()
    {
        await Create().HandleAsync(Donor());
        await Create().HandleAsync(Donor());

        _store.Donors.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("", null, "DONOR_NAME_INVALID")]
    [InlineData("A", "not-an-email", "DONOR_EMAIL_INVALID")]
    [InlineData("A", "@x.vn", "DONOR_EMAIL_INVALID")]
    public async Task Fields_are_validated(string name, string? email, string code)
    {
        (await Fails(() => Create().HandleAsync(Donor(name, email: email)))).Code.Should().Be(code);
    }

    [Fact]
    public async Task Officer_cannot_record_for_another_parish_and_admin_must_choose_one()
    {
        (await Fails(() => Create().HandleAsync(Donor(parishId: OtherParish)))).Code.Should().Be("PARISH_SCOPE_DENIED");
        (await Fails(() => Create(Admin()).HandleAsync(Donor()))).Code.Should().Be("DONOR_PARISH_REQUIRED");

        (await Create(Admin()).HandleAsync(Donor(parishId: OtherParish))).ParishId.Should().Be(OtherParish);
    }

    [Fact]
    public async Task Account_link_must_exist_in_the_same_parish_and_be_unique()
    {
        var volunteer = _identity.Seed(Parish);
        var outsider = _identity.Seed(OtherParish);

        (await Create().HandleAsync(Donor(userId: volunteer.UserId))).UserId.Should().Be(volunteer.UserId);

        (await Fails(() => Create().HandleAsync(Donor(userId: volunteer.UserId)))).Code.Should().Be("DONOR_USER_ALREADY_LINKED");
        (await Fails(() => Create().HandleAsync(Donor(userId: outsider.UserId)))).Code.Should().Be("DONOR_USER_OTHER_PARISH");
        (await Fails(() => Create().HandleAsync(Donor(userId: Guid.NewGuid())))).Code.Should().Be("DONOR_USER_NOT_FOUND");
    }

    [Fact]
    public async Task Identity_down_is_a_503_not_a_silent_link()
    {
        _identity.Unavailable = true;

        var failure = await Fails(() => Create().HandleAsync(Donor(userId: Guid.NewGuid())));

        failure.Code.Should().Be("IDENTITY_UNAVAILABLE");
        failure.Kind.Should().Be(DomainErrorKind.ServiceUnavailable);
        _store.Donors.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_changes_contact_details_and_clears_empty_fields()
    {
        var created = await Create().HandleAsync(Donor(email: "lan@example.com"));

        var updated = await Update().HandleAsync(new UpdateDonorCommand(created.Id, created.Version, "Gia đình bà Lan", "", null, "12 Lê Lợi", null));

        updated.DonorName.Should().Be("Gia đình bà Lan");
        updated.Phone.Should().BeNull();
        updated.Email.Should().Be("lan@example.com");
        updated.Address.Should().Be("12 Lê Lợi");
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_rejects_stale_versions_and_other_parishes()
    {
        var created = await Create().HandleAsync(Donor());

        (await Fails(() => Update().HandleAsync(new UpdateDonorCommand(created.Id, created.Version + 1, "X", null, null, null, null))))
            .Kind.Should().Be(DomainErrorKind.PreconditionFailed);
        (await Fails(() => Update(Officer(OtherParish)).HandleAsync(new UpdateDonorCommand(created.Id, null, "X", null, null, null, null))))
            .Code.Should().Be("DONOR_NOT_FOUND");
        (await Fails(() => Update().HandleAsync(new UpdateDonorCommand(created.Id, null, " ", null, null, null, null))))
            .Code.Should().Be("DONOR_NAME_INVALID");
    }

    [Fact]
    public async Task List_is_limited_to_the_parish_and_searches_name_phone_email()
    {
        await Create().HandleAsync(Donor("Bà Maria Lan", phone: "0901"));
        await Create().HandleAsync(Donor("Ông Giuse Hùng", phone: "0988", email: "hung@example.com"));
        await Create(Officer(OtherParish)).HandleAsync(Donor("Bà Maria Lan khác"));

        (await List().HandleAsync(new DonorFilter(1, 20, null, null, null, DonorSortField.Name, false))).TotalItems.Should().Be(2);
        (await List().HandleAsync(new DonorFilter(1, 20, null, "hung@", null, DonorSortField.Name, false)))
            .Items.Should().ContainSingle().Which.DonorName.Should().Be("Ông Giuse Hùng");
        (await List(Admin()).HandleAsync(new DonorFilter(1, 20, null, "Maria", null, DonorSortField.Name, false))).TotalItems.Should().Be(2);
        (await List(Admin()).HandleAsync(new DonorFilter(1, 20, OtherParish, null, null, DonorSortField.Name, false))).TotalItems.Should().Be(1);
    }
}
