using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MapHub.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Caption",
                table: "PlaceImages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "FoodRating",
                table: "PlaceReviews",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShareToken",
                table: "Plans",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Plans_ShareToken')
                BEGIN
                    CREATE UNIQUE INDEX IX_Plans_ShareToken ON Plans(ShareToken)
                    WHERE ShareToken IS NOT NULL;
                END
            ");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Plans",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Caption",     table: "PlaceImages");
            migrationBuilder.DropColumn(name: "FoodRating",  table: "PlaceReviews");
            migrationBuilder.DropColumn(name: "ShareToken",  table: "Plans");
            migrationBuilder.DropColumn(name: "IsPublic",    table: "Plans");
        }
    }
}
