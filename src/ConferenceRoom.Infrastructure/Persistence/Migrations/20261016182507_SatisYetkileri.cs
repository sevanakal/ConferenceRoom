using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ConferenceRoom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SatisYetkileri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SeatsRestricted",
                table: "StaffUsers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SeatScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    HallId = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<int>(type: "int", nullable: true),
                    ScopeJson = table.Column<string>(type: "longtext", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedByStaffUserId = table.Column<int>(type: "int", nullable: false),
                    EventKey = table.Column<int>(type: "int", nullable: false, computedColumnSql: "COALESCE(`EventId`, 0)", stored: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeatScopes_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeatScopes_Halls_HallId",
                        column: x => x.HallId,
                        principalTable: "Halls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeatScopes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeatScopes_StaffUsers_UpdatedByStaffUserId",
                        column: x => x.UpdatedByStaffUserId,
                        principalTable: "StaffUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Permission", "RoleId" },
                values: new object[,]
                {
                    { "sales.sell", 1 },
                    { "sales.sell", 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeatScopes_EventId",
                table: "SeatScopes",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatScopes_HallId_EventKey",
                table: "SeatScopes",
                columns: new[] { "HallId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeatScopes_OrganizationId",
                table: "SeatScopes",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatScopes_UpdatedByStaffUserId",
                table: "SeatScopes",
                column: "UpdatedByStaffUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeatScopes");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "sales.sell", 1 });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "sales.sell", 3 });

            migrationBuilder.DropColumn(
                name: "SeatsRestricted",
                table: "StaffUsers");
        }
    }
}
