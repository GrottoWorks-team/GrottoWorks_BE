using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FIDN01_InitialIdentitySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    refresh_token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token", x => x.refresh_token_id);
                    table.ForeignKey(
                        name: "FK_refresh_token_refresh_token_replaced_by_token_id",
                        column: x => x.replaced_by_token_id,
                        principalTable: "refresh_token",
                        principalColumn: "refresh_token_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    role_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role_description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role", x => x.role_id);
                });

            migrationBuilder.CreateTable(
                name: "skill",
                columns: table => new
                {
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    skill_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    skill_description = table.Column<string>(type: "text", nullable: true),
                    skill_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill", x => x.skill_id);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parish_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    user_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_app_user_role_role_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "role_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "volunteer_availability",
                columns: table => new
                {
                    volunteer_availability_id = table.Column<Guid>(type: "uuid", nullable: false),
                    volunteer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    available_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    availability_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volunteer_availability", x => x.volunteer_availability_id);
                    table.CheckConstraint("volunteer_availability_valid_range", "\"available_to\" > \"available_from\"");
                    table.ForeignKey(
                        name: "FK_volunteer_availability_app_user_volunteer_user_id",
                        column: x => x.volunteer_user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "volunteer_profile",
                columns: table => new
                {
                    volunteer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    community_id = table.Column<Guid>(type: "uuid", nullable: false),
                    introduction = table.Column<string>(type: "text", nullable: true),
                    availability_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volunteer_profile", x => x.volunteer_user_id);
                    table.ForeignKey(
                        name: "FK_volunteer_profile_app_user_volunteer_user_id",
                        column: x => x.volunteer_user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "volunteer_skill",
                columns: table => new
                {
                    volunteer_skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    volunteer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_level = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    skill_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volunteer_skill", x => x.volunteer_skill_id);
                    table.ForeignKey(
                        name: "FK_volunteer_skill_app_user_volunteer_user_id",
                        column: x => x.volunteer_user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_volunteer_skill_skill_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skill",
                        principalColumn: "skill_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_user_role_id",
                table: "app_user",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_app_user_email",
                table: "app_user",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_family_id",
                table: "refresh_token",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_replaced_by_token_id",
                table: "refresh_token",
                column: "replaced_by_token_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_token_token_hash",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_role_role_code",
                table: "role",
                column: "role_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_skill_skill_code",
                table: "skill",
                column: "skill_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_volunteer_availability_volunteer_user_id",
                table: "volunteer_availability",
                column: "volunteer_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_volunteer_skill_skill_id",
                table: "volunteer_skill",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ux_volunteer_skill_user_skill",
                table: "volunteer_skill",
                columns: new[] { "volunteer_user_id", "skill_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_token");

            migrationBuilder.DropTable(
                name: "volunteer_availability");

            migrationBuilder.DropTable(
                name: "volunteer_profile");

            migrationBuilder.DropTable(
                name: "volunteer_skill");

            migrationBuilder.DropTable(
                name: "app_user");

            migrationBuilder.DropTable(
                name: "skill");

            migrationBuilder.DropTable(
                name: "role");
        }
    }
}
