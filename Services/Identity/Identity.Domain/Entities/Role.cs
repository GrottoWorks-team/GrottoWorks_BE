namespace Identity.Domain.Entities;

public sealed class Role
{
    private Role()
    {
    }

    private Role(Guid roleId, string code, string name, string? description)
    {
        RoleId = roleId;
        Code = code;
        Name = name;
        Description = description;
    }

    public static Role Create(string code, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("ROLE_CODE_REQUIRED", "Role code is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("ROLE_NAME_REQUIRED", "Role name is required.");
        }

        return new Role(
            Guid.NewGuid(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }

    public Guid RoleId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }
}
