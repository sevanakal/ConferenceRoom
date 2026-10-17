using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ConferenceRoom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Satislar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    EventSessionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Channel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    BuyerName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    BuyerPhone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Payment = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    SoldByStaffUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_EventSessions_EventSessionId",
                        column: x => x.EventSessionId,
                        principalTable: "EventSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_StaffUsers_SoldByStaffUserId",
                        column: x => x.SoldByStaffUserId,
                        principalTable: "StaffUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    EventSessionId = table.Column<int>(type: "int", nullable: false),
                    SeatId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    SeatLabel = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    CategoryName = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tickets_EventSessions_EventSessionId",
                        column: x => x.EventSessionId,
                        principalTable: "EventSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Tickets_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_EventId_CreatedAtUtc",
                table: "Orders",
                columns: new[] { "EventId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_EventSessionId",
                table: "Orders",
                column: "EventSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrganizationId",
                table: "Orders",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SoldByStaffUserId",
                table: "Orders",
                column: "SoldByStaffUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_EventSessionId_SeatId",
                table: "Tickets",
                columns: new[] { "EventSessionId", "SeatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_OrderId",
                table: "Tickets",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_OrganizationId",
                table: "Tickets",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tickets");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
