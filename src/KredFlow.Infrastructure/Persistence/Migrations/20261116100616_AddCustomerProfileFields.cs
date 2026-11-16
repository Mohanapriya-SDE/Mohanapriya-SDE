using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KredFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_LenderId",
                table: "Customers");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Customers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_LenderId_FullName",
                table: "Customers",
                columns: new[] { "LenderId", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_LenderId_PhoneNumber",
                table: "Customers",
                columns: new[] { "LenderId", "PhoneNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_LenderId_FullName",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_LenderId_PhoneNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_LenderId",
                table: "Customers",
                column: "LenderId");
        }
    }
}
