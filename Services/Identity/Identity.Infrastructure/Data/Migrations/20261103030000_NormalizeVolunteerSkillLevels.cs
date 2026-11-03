using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Identity.Infrastructure.Data.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20261103030000_NormalizeVolunteerSkillLevels")]
public sealed class NormalizeVolunteerSkillLevels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$
        DECLARE unsupported text;
        BEGIN
          SELECT string_agg(DISTINCT skill_level, ', ') INTO unsupported FROM volunteer_skill
          WHERE NULLIF(trim(skill_level), '') IS NOT NULL
            AND upper(trim(skill_level)) NOT IN ('BASIC','INTERMEDIATE','ADVANCED','EXPERT');
          IF unsupported IS NOT NULL THEN
            RAISE EXCEPTION 'Unmapped volunteer skill levels: %. Map these values explicitly before retrying migration.', unsupported;
          END IF;
        END $$;
        UPDATE volunteer_skill SET skill_level = upper(NULLIF(trim(skill_level), ''));
        ALTER TABLE volunteer_skill ADD CONSTRAINT volunteer_skill_level_valid
          CHECK (skill_level IS NULL OR skill_level IN ('BASIC','INTERMEDIATE','ADVANCED','EXPERT'));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropCheckConstraint("volunteer_skill_level_valid", "volunteer_skill");
}
