using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehicleManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class VehicleFuelType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FuelType",
                table: "Vehicles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unleaded");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FuelType",
                table: "Vehicles");
        }
    }
}
