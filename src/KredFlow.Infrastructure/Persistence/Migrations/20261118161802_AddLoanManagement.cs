using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KredFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepaymentSchedules_LoanId_InstallmentNumber",
                table: "RepaymentSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Loans_LenderId",
                table: "Loans");

            migrationBuilder.AddColumn<bool>(
                name: "IsSuperseded",
                table: "RepaymentSchedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningPrincipal",
                table: "RepaymentSchedules",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "Payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Regular");

            migrationBuilder.AlterColumn<Guid>(
                name: "ScheduleId",
                table: "PaymentAllocations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "PaymentAllocations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Installment");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ClosedDate",
                table: "Loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureRemarks",
                table: "Loans",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Loans",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "PrepaymentPolicy",
                table: "Loans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "LenderChoice");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Loans",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill existing rows: derive the new values from data that is already stored.
            migrationBuilder.Sql("""
                UPDATE "Loans" SET "CreatedAt" = "StartDate"::timestamp AT TIME ZONE 'UTC';
                UPDATE "Payments" SET "CreatedAt" = "PaymentDate"::timestamp AT TIME ZONE 'UTC';
                UPDATE "RepaymentSchedules" rs SET "OpeningPrincipal" = (
                    SELECT COALESCE(SUM(x."PrincipalDue"), 0) FROM "RepaymentSchedules" x
                    WHERE x."LoanId" = rs."LoanId" AND x."InstallmentNumber" >= rs."InstallmentNumber");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RepaymentSchedules_LoanId_InstallmentNumber",
                table: "RepaymentSchedules",
                columns: new[] { "LoanId", "InstallmentNumber" },
                unique: true,
                filter: "\"IsSuperseded\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_LenderId_CustomerId",
                table: "Loans",
                columns: new[] { "LenderId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_LenderId_Status_StartDate",
                table: "Loans",
                columns: new[] { "LenderId", "Status", "StartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepaymentSchedules_LoanId_InstallmentNumber",
                table: "RepaymentSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Loans_LenderId_CustomerId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Loans_LenderId_Status_StartDate",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "IsSuperseded",
                table: "RepaymentSchedules");

            migrationBuilder.DropColumn(
                name: "OpeningPrincipal",
                table: "RepaymentSchedules");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "ClosedDate",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ClosureRemarks",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "PrepaymentPolicy",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Loans");

            migrationBuilder.AlterColumn<Guid>(
                name: "ScheduleId",
                table: "PaymentAllocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepaymentSchedules_LoanId_InstallmentNumber",
                table: "RepaymentSchedules",
                columns: new[] { "LoanId", "InstallmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loans_LenderId",
                table: "Loans",
                column: "LenderId");
        }
    }
}
