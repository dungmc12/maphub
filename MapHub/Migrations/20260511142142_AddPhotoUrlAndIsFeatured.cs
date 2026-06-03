using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MapHub.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoUrlAndIsFeatured : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFeatured",
                table: "Places",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "PlaceReviews",
                type: "nvarchar(max)",
                nullable: true);

            // Narrow ShareToken to support unique index
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='Plans' AND COLUMN_NAME='ShareToken'
                      AND CHARACTER_MAXIMUM_LENGTH = -1
                )
                    ALTER TABLE Plans ALTER COLUMN ShareToken nvarchar(450) NULL;
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Plans_ShareToken')
                    CREATE UNIQUE INDEX IX_Plans_ShareToken ON Plans(ShareToken)
                    WHERE ShareToken IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsFeatured", table: "Places");
            migrationBuilder.DropColumn(name: "PhotoUrl",   table: "PlaceReviews");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Plans_ShareToken')
                    DROP INDEX IX_Plans_ShareToken ON Plans;
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='Plans' AND COLUMN_NAME='ShareToken'
                      AND CHARACTER_MAXIMUM_LENGTH = 450
                )
                    ALTER TABLE Plans ALTER COLUMN ShareToken nvarchar(max) NULL;
            ");
        }
    }
}
