using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaskExecution.Infrastructure.Data.Migrations;

[DbContext(typeof(TaskExecutionDbContext))]
[Migration("20261103110000_NormalizeRequiredSkillLevels")]
public sealed class NormalizeRequiredSkillLevels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$
        DECLARE unsupported text;
        BEGIN
          SELECT string_agg(DISTINCT required_skill_level, ', ') INTO unsupported FROM (
            SELECT required_skill_level FROM task_skill
            UNION ALL SELECT required_skill_level FROM support_position_skill
          ) levels WHERE NULLIF(trim(required_skill_level), '') IS NOT NULL
            AND upper(trim(required_skill_level)) NOT IN ('BASIC','INTERMEDIATE','ADVANCED','EXPERT');
          IF unsupported IS NOT NULL THEN
            RAISE EXCEPTION 'Unmapped required skill levels: %. Map these values explicitly before retrying migration.', unsupported;
          END IF;
        END $$;
        UPDATE task_skill SET required_skill_level = upper(NULLIF(trim(required_skill_level), ''));
        UPDATE support_position_skill SET required_skill_level = upper(NULLIF(trim(required_skill_level), ''));
        ALTER TABLE task_skill ADD CONSTRAINT task_skill_level_valid
          CHECK (required_skill_level IS NULL OR required_skill_level IN ('BASIC','INTERMEDIATE','ADVANCED','EXPERT'));
        ALTER TABLE support_position_skill ADD CONSTRAINT support_skill_level_valid
          CHECK (required_skill_level IS NULL OR required_skill_level IN ('BASIC','INTERMEDIATE','ADVANCED','EXPERT'));
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("task_skill_level_valid", "task_skill");
        migrationBuilder.DropCheckConstraint("support_skill_level_valid", "support_position_skill");
    }
}
