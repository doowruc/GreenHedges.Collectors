using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GreenHedges.HomeServer.VehicleManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class StationsBelongToBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Station names become unique per brand rather than per user.
            migrationBuilder.DropIndex(
                name: "IX_FuelStations_UserId_Name",
                table: "FuelStations");

            migrationBuilder.AddColumn<int>(
                name: "FuelBrandId",
                table: "FuelStations",
                type: "int",
                nullable: true);

            // Give every station a brand, from the brands recorded on its fill-ups. One batch,
            // because it uses a temp table.
            migrationBuilder.Sql("""
                DECLARE @now datetime2 = SYSUTCDATETIME();

                -- Every stop now needs a station, and every station a brand. A stop recorded with a
                -- station but no brand takes the brand that station is most often used with; anything
                -- still missing gets an "Unknown" brand or station. (There were none when this was written.)
                UPDATE s SET FuelBrandId = (
                    SELECT TOP 1 o.FuelBrandId FROM FuelEntryStops o
                    WHERE o.FuelStationId = s.FuelStationId AND o.FuelBrandId IS NOT NULL
                    GROUP BY o.FuelBrandId ORDER BY COUNT(*) DESC, o.FuelBrandId)
                FROM FuelEntryStops s
                WHERE s.FuelBrandId IS NULL AND s.FuelStationId IS NOT NULL;

                INSERT INTO FuelBrands (UserId, Name, AddedDate, AddedBy, ModifiedDate, ModifiedBy)
                SELECT DISTINCT v.UserId, N'Unknown', @now, v.UserId, @now, v.UserId
                FROM FuelEntryStops s JOIN FuelEntries f ON f.Id = s.FuelEntryId JOIN Vehicles v ON v.Id = f.VehicleId
                WHERE s.FuelBrandId IS NULL
                  AND NOT EXISTS (SELECT 1 FROM FuelBrands b WHERE b.UserId = v.UserId AND b.Name = N'Unknown');
                UPDATE s SET FuelBrandId = b.Id
                FROM FuelEntryStops s JOIN FuelEntries f ON f.Id = s.FuelEntryId JOIN Vehicles v ON v.Id = f.VehicleId
                JOIN FuelBrands b ON b.UserId = v.UserId AND b.Name = N'Unknown'
                WHERE s.FuelBrandId IS NULL;

                INSERT INTO FuelStations (UserId, Name, AddedDate, AddedBy, ModifiedDate, ModifiedBy)
                SELECT DISTINCT v.UserId, N'Unknown', @now, v.UserId, @now, v.UserId
                FROM FuelEntryStops s JOIN FuelEntries f ON f.Id = s.FuelEntryId JOIN Vehicles v ON v.Id = f.VehicleId
                WHERE s.FuelStationId IS NULL
                  AND NOT EXISTS (SELECT 1 FROM FuelStations st WHERE st.UserId = v.UserId AND st.Name = N'Unknown');
                UPDATE s SET FuelStationId = st.Id
                FROM FuelEntryStops s JOIN FuelEntries f ON f.Id = s.FuelEntryId JOIN Vehicles v ON v.Id = f.VehicleId
                JOIN FuelStations st ON st.UserId = v.UserId AND st.Name = N'Unknown'
                WHERE s.FuelStationId IS NULL;

                -- Each location becomes one station per brand used there. The brand used most keeps
                -- the existing station; each other brand gets a new station with the same name.
                CREATE TABLE #Pairs (StationId int NOT NULL, BrandId int NOT NULL, Rank int NOT NULL);
                INSERT INTO #Pairs (StationId, BrandId, Rank)
                SELECT FuelStationId, FuelBrandId,
                       ROW_NUMBER() OVER (PARTITION BY FuelStationId ORDER BY COUNT(*) DESC, MIN(Id))
                FROM FuelEntryStops
                GROUP BY FuelStationId, FuelBrandId;

                UPDATE st SET FuelBrandId = p.BrandId
                FROM FuelStations st JOIN #Pairs p ON p.StationId = st.Id AND p.Rank = 1;

                INSERT INTO FuelStations (UserId, FuelBrandId, Name, AddedDate, AddedBy, ModifiedDate, ModifiedBy)
                SELECT st.UserId, p.BrandId, st.Name, @now, st.UserId, @now, st.UserId
                FROM #Pairs p JOIN FuelStations st ON st.Id = p.StationId
                WHERE p.Rank > 1;

                UPDATE s SET FuelStationId = n.Id, ModifiedDate = @now
                FROM FuelEntryStops s
                JOIN #Pairs p ON p.StationId = s.FuelStationId AND p.BrandId = s.FuelBrandId AND p.Rank > 1
                JOIN FuelStations old ON old.Id = s.FuelStationId
                JOIN FuelStations n ON n.UserId = old.UserId AND n.Name = old.Name AND n.FuelBrandId = p.BrandId AND n.Id <> old.Id;

                DROP TABLE #Pairs;

                -- A station no fill-up uses has no brand to take, so it can't be kept.
                DELETE FROM FuelStations WHERE FuelBrandId IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "FuelBrandId",
                table: "FuelStations",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelStations_FuelBrandId_Name",
                table: "FuelStations",
                columns: new[] { "FuelBrandId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FuelStations_FuelBrands_FuelBrandId",
                table: "FuelStations",
                column: "FuelBrandId",
                principalTable: "FuelBrands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // A stop's brand now comes from its station.
            migrationBuilder.DropForeignKey(
                name: "FK_FuelEntryStops_FuelBrands_FuelBrandId",
                table: "FuelEntryStops");

            migrationBuilder.DropIndex(
                name: "IX_FuelEntryStops_FuelBrandId",
                table: "FuelEntryStops");

            migrationBuilder.DropColumn(
                name: "FuelBrandId",
                table: "FuelEntryStops");

            migrationBuilder.AlterColumn<int>(
                name: "FuelStationId",
                table: "FuelEntryStops",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FuelBrandId",
                table: "FuelEntryStops",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "FuelStationId",
                table: "FuelEntryStops",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            // Copy each stop's brand from its station, then combine stations that share a name
            // (station names were unique per user before), keeping the oldest.
            migrationBuilder.Sql("""
                UPDATE s SET FuelBrandId = st.FuelBrandId
                FROM FuelEntryStops s JOIN FuelStations st ON st.Id = s.FuelStationId;

                UPDATE s SET FuelStationId = keep.Id
                FROM FuelEntryStops s
                JOIN FuelStations st ON st.Id = s.FuelStationId
                CROSS APPLY (SELECT MIN(k.Id) AS Id FROM FuelStations k WHERE k.UserId = st.UserId AND k.Name = st.Name) keep
                WHERE keep.Id <> st.Id;

                DELETE st FROM FuelStations st
                WHERE EXISTS (SELECT 1 FROM FuelStations k WHERE k.UserId = st.UserId AND k.Name = st.Name AND k.Id < st.Id);
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_FuelStations_FuelBrands_FuelBrandId",
                table: "FuelStations");

            migrationBuilder.DropIndex(
                name: "IX_FuelStations_FuelBrandId_Name",
                table: "FuelStations");

            migrationBuilder.DropColumn(
                name: "FuelBrandId",
                table: "FuelStations");

            migrationBuilder.CreateIndex(
                name: "IX_FuelStations_UserId_Name",
                table: "FuelStations",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelEntryStops_FuelBrandId",
                table: "FuelEntryStops",
                column: "FuelBrandId");

            migrationBuilder.AddForeignKey(
                name: "FK_FuelEntryStops_FuelBrands_FuelBrandId",
                table: "FuelEntryStops",
                column: "FuelBrandId",
                principalTable: "FuelBrands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
