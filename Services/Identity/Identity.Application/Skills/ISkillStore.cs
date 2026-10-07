using Identity.Domain.Entities;

namespace Identity.Application.Skills;

/// <summary>Skill catalog store (F-IDN-06).</summary>
public interface ISkillStore
{
    Task<IReadOnlyList<Skill>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Skill?> GetByIdAsync(Guid skillId, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(Skill skill);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
