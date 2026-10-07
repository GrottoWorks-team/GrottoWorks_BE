using Identity.Application.Skills;
using Identity.Domain.Entities;
using Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Skills;

public sealed class SkillStore(IdentityDbContext dbContext) : ISkillStore
{
    public async Task<IReadOnlyList<Skill>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Skills
            .AsNoTracking()
            .OrderBy(skill => skill.Name)
            .ThenBy(skill => skill.SkillId)
            .ToListAsync(cancellationToken);
    }

    public Task<Skill?> GetByIdAsync(Guid skillId, CancellationToken cancellationToken = default)
    {
        return dbContext.Skills.FirstOrDefaultAsync(skill => skill.SkillId == skillId, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(
        string normalizedCode,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Skills.AnyAsync(skill => skill.Code == normalizedCode, cancellationToken);
    }

    public void Add(Skill skill)
    {
        dbContext.Skills.Add(skill);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
