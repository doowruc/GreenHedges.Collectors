using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehicleManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class ServiceDueMileage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SentReminders_VehicleId_Kind_DueDate_DaysBefore",
                table: "SentReminders");

            migrationBuilder.AddColumn<int>(
                name: "ServiceDueMileage",
                table: "Vehicles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DueMileage",
                table: "SentReminders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentReminders_VehicleId_Kind_DueDate_DueMileage_DaysBefore",
                table: "SentReminders",
                columns: new[] { "VehicleId", "Kind", "DueDate", "DueMileage", "DaysBefore" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SentReminders_VehicleId_Kind_DueDate_DueMileage_DaysBefore",
                table: "SentReminders");

            migrationBuilder.DropColumn(
                name: "ServiceDueMileage",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "DueMileage",
                table: "SentReminders");

            migrationBuilder.CreateIndex(
                name: "IX_SentReminders_VehicleId_Kind_DueDate_DaysBefore",
                table: "SentReminders",
                columns: new[] { "VehicleId", "Kind", "DueDate", "DaysBefore" },
                unique: true);
        }
    }
}
