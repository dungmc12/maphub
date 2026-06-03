using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MapHub.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LƯU Ý: các cột Plans.Description, PlanItems.ArrivalTime/DayNumber/LeaveTime
            // đã tồn tại sẵn trong DB (tính năng lịch trình trước đó được thêm thủ công,
            // chưa có migration). Đã bỏ AddColumn cho chúng để tránh lỗi "column already exists".
            // Model snapshot vẫn ghi nhận đầy đủ nên EF không tạo lại migration cho chúng.

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanType",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "month");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PlanType",
                table: "Payments");
        }
    }
}
