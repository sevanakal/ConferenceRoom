using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConferenceRoom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BiletKategoriKimligi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryId",
                table: "Tickets",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Tickets");
        }
    }
}
