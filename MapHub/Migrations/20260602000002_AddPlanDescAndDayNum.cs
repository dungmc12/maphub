using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MapHub.Migrations
{
    public partial class AddPlanDescAndDayNum : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Plans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DayNumber",
                table: "PlanItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArrivalTime",
                table: "PlanItems",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaveTime",
                table: "PlanItems",
                type: "time",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Description",  table: "Plans");
            migrationBuilder.DropColumn(name: "DayNumber",    table: "PlanItems");
            migrationBuilder.DropColumn(name: "ArrivalTime",  table: "PlanItems");
            migrationBuilder.DropColumn(name: "LeaveTime",    table: "PlanItems");
        }
    }
}
