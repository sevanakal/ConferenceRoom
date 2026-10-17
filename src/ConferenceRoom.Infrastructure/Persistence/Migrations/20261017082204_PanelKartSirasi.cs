using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConferenceRoom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PanelKartSirasi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DashboardOrder",
                table: "StaffUsers",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DashboardOrder",
                table: "StaffUsers");
        }
    }
}
