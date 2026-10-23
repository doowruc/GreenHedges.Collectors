using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GreenHedges.HomeServer.VehicleManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class FuelBrandsAndStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FuelBrands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AddedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelBrands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FuelStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AddedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelStations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FuelEntryStops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FuelEntryId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    FuelBrandId = table.Column<int>(type: "int", nullable: true),
                    FuelStationId = table.Column<int>(type: "int", nullable: true),
                    AddedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelEntryStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FuelEntryStops_FuelBrands_FuelBrandId",
                        column: x => x.FuelBrandId,
                        principalTable: "FuelBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FuelEntryStops_FuelEntries_FuelEntryId",
                        column: x => x.FuelEntryId,
                        principalTable: "FuelEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FuelEntryStops_FuelStations_FuelStationId",
                        column: x => x.FuelStationId,
                        principalTable: "FuelStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FuelBrands_UserId_Name",
                table: "FuelBrands",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelEntryStops_FuelBrandId",
                table: "FuelEntryStops",
                column: "FuelBrandId");

            migrationBuilder.CreateIndex(
                name: "IX_FuelEntryStops_FuelEntryId_Sequence",
                table: "FuelEntryStops",
                columns: new[] { "FuelEntryId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelEntryStops_FuelStationId",
                table: "FuelEntryStops",
                column: "FuelStationId");

            migrationBuilder.CreateIndex(
                name: "IX_FuelStations_UserId_Name",
                table: "FuelStations",
                columns: new[] { "UserId", "Name" },
                unique: true);

            // Move the existing Brand/Location text into the new tables. A split fill-up lists its
            // brands and locations in the same order ("Bx + By" / "Lx + Ly"), so the parts pair up by
            // position. One batch, because it uses a temp table.
            migrationBuilder.Sql("""
                CREATE TABLE #Stops (
                    FuelEntryId int NOT NULL,
                    UserId nvarchar(128) COLLATE DATABASE_DEFAULT NOT NULL,
                    Sequence int NOT NULL,
                    Brand nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                    Station nvarchar(100) COLLATE DATABASE_DEFAULT NULL);

                WITH Parts AS (
                    SELECT f.Id AS FuelEntryId, v.UserId, 'B' AS Kind, CAST(p.ordinal AS int) AS Sequence, TRIM(p.value) AS Name
                    FROM FuelEntries f
                    JOIN Vehicles v ON v.Id = f.VehicleId
                    CROSS APPLY STRING_SPLIT(f.Brand, '+', 1) p
                    UNION ALL
                    SELECT f.Id, v.UserId, 'L', CAST(p.ordinal AS int), TRIM(p.value)
                    FROM FuelEntries f
                    JOIN Vehicles v ON v.Id = f.VehicleId
                    CROSS APPLY STRING_SPLIT(f.Location, '+', 1) p
                )
                INSERT INTO #Stops (FuelEntryId, UserId, Sequence, Brand, Station)
                SELECT FuelEntryId, UserId, Sequence,
                       NULLIF(MAX(CASE WHEN Kind = 'B' THEN Name END), ''),
                       NULLIF(MAX(CASE WHEN Kind = 'L' THEN Name END), '')
                FROM Parts
                GROUP BY FuelEntryId, UserId, Sequence
                HAVING MAX(Name) <> '';

                -- One brand/station per name per user (names compare ignoring case, like the app).
                DECLARE @now datetime2 = SYSUTCDATETIME();

                INSERT INTO FuelBrands (UserId, Name, AddedDate, AddedBy, ModifiedDate, ModifiedBy)
                SELECT UserId, MIN(Brand), @now, UserId, @now, UserId
                FROM #Stops WHERE Brand IS NOT NULL
                GROUP BY UserId, Brand;

                INSERT INTO FuelStations (UserId, Name, AddedDate, AddedBy, ModifiedDate, ModifiedBy)
                SELECT UserId, MIN(Station), @now, UserId, @now, UserId
                FROM #Stops WHERE Station IS NOT NULL
                GROUP BY UserId, Station;

                INSERT INTO FuelEntryStops (FuelEntryId, Sequence, FuelBrandId, FuelStationId, AddedDate, AddedBy, ModifiedDate, ModifiedBy)
                SELECT s.FuelEntryId, ROW_NUMBER() OVER (PARTITION BY s.FuelEntryId ORDER BY s.Sequence),
                       b.Id, st.Id, @now, s.UserId, @now, s.UserId
                FROM #Stops s
                LEFT JOIN FuelBrands b ON b.UserId = s.UserId AND b.Name = s.Brand
                LEFT JOIN FuelStations st ON st.UserId = s.UserId AND st.Name = s.Station;

                DROP TABLE #Stops;
                """);

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "FuelEntries");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "FuelEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "FuelEntries",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "FuelEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Rebuild the text columns ("Bx + By" for a split fill-up). Merged names stay merged.
            migrationBuilder.Sql("""
                UPDATE f SET
                    Brand = (SELECT LEFT(STRING_AGG(b.Name, ' + ') WITHIN GROUP (ORDER BY s.Sequence), 50)
                             FROM FuelEntryStops s JOIN FuelBrands b ON b.Id = s.FuelBrandId
                             WHERE s.FuelEntryId = f.Id),
                    Location = (SELECT LEFT(STRING_AGG(st.Name, ' + ') WITHIN GROUP (ORDER BY s.Sequence), 100)
                                FROM FuelEntryStops s JOIN FuelStations st ON st.Id = s.FuelStationId
                                WHERE s.FuelEntryId = f.Id)
                FROM FuelEntries f;
                """);

            migrationBuilder.DropTable(
                name: "FuelEntryStops");

            migrationBuilder.DropTable(
                name: "FuelBrands");

            migrationBuilder.DropTable(
                name: "FuelStations");

        }
    }
}
