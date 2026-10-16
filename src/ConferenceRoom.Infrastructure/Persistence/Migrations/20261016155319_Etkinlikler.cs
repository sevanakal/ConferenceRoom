using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ConferenceRoom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Etkinlikler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    DefaultSaleMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DefaultRequiresApproval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventTypes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    Organizer = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    EventTypeId = table.Column<int>(type: "int", nullable: false),
                    HallId = table.Column<int>(type: "int", nullable: false),
                    PosterFileName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    SaleMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RequiresApproval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OnlineEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SalesStartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SalesEndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    MaxTicketsPerPerson = table.Column<int>(type: "int", nullable: true),
                    CreatedByStaffUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_EventTypes_EventTypeId",
                        column: x => x.EventTypeId,
                        principalTable: "EventTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_Halls_HallId",
                        column: x => x.HallId,
                        principalTable: "Halls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_StaffUsers_CreatedByStaffUserId",
                        column: x => x.CreatedByStaffUserId,
                        principalTable: "StaffUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EventSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DoorsOpenAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    HallPlanVersionId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSessions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventSessions_HallPlanVersions_HallPlanVersionId",
                        column: x => x.HallPlanVersionId,
                        principalTable: "HallPlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "EventTypes",
                columns: new[] { "Id", "CreatedAtUtc", "DefaultRequiresApproval", "DefaultSaleMode", "IsActive", "Name", "OrganizationId" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), false, "Reservation", true, "Konferans", 1 },
                    { 2, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), false, "Sale", true, "Konser", 1 },
                    { 3, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), false, "Sale", true, "Tiyatro", 1 },
                    { 4, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), true, "Reservation", true, "Seminer", 1 },
                    { 5, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), true, "Reservation", true, "Toplantı", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_CreatedByStaffUserId",
                table: "Events",
                column: "CreatedByStaffUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventTypeId",
                table: "Events",
                column: "EventTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_HallId",
                table: "Events",
                column: "HallId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_OrganizationId_Status",
                table: "Events",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EventSessions_EventId",
                table: "EventSessions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSessions_HallPlanVersionId",
                table: "EventSessions",
                column: "HallPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSessions_StartsAtUtc",
                table: "EventSessions",
                column: "StartsAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EventTypes_OrganizationId_Name",
                table: "EventTypes",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventSessions");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "EventTypes");
        }
    }
}
