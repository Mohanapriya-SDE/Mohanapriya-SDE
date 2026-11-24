using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KredFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_LoanId",
                table: "Payments");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByPaymentId",
                table: "RepaymentSchedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByPaymentId",
                table: "RepaymentSchedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PenaltyAmount",
                table: "PaymentAllocations",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PenaltyId",
                table: "PaymentAllocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceId",
                table: "LoanTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LoanRecasts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Treatment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviousEmiAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousTenureMonths = table.Column<int>(type: "integer", nullable: false),
                    NewEmiAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NewTenureMonths = table.Column<int>(type: "integer", nullable: false),
                    PrincipalOutstandingAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanRecasts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanRecasts_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReversals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReversalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentReversals_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Penalties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AssessedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Penalties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Penalties_Loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "Loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Penalties_RepaymentSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "RepaymentSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PenaltyWaivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PenaltyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltyWaivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenaltyWaivers_Penalties_PenaltyId",
                        column: x => x.PenaltyId,
                        principalTable: "Penalties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Backfill: penalties and waivers used to exist only as ledger entries. Each Penalty entry becomes a Penalties
            // row that reuses the ledger entry's id, and each Waiver entry is spread over penalties oldest first.
            migrationBuilder.Sql("""
                INSERT INTO "Penalties" ("Id", "LoanId", "ScheduleId", "Amount", "AssessedDate", "CreatedAt", "CreatedByUserId")
                SELECT "Id", "LoanId", NULL, "Amount", "TransactionDate", "TransactionDate"::timestamp AT TIME ZONE 'UTC', NULL
                FROM "LoanTransactions" WHERE "TransactionType" = 'Penalty';

                UPDATE "LoanTransactions" SET "ReferenceId" = "Id" WHERE "TransactionType" = 'Penalty';

                DO $$
                DECLARE w RECORD; p RECORD; remaining numeric; applied numeric;
                BEGIN
                  FOR w IN SELECT "LoanId", "Amount", "TransactionDate" FROM "LoanTransactions"
                           WHERE "TransactionType" = 'Waiver' ORDER BY "TransactionDate", "Id" LOOP
                    remaining := w."Amount";
                    FOR p IN SELECT pe."Id" AS id,
                                    pe."Amount" - COALESCE((SELECT SUM(pw."Amount") FROM "PenaltyWaivers" pw WHERE pw."PenaltyId" = pe."Id"), 0) AS outstanding
                             FROM "Penalties" pe WHERE pe."LoanId" = w."LoanId" ORDER BY pe."AssessedDate", pe."Id" LOOP
                      EXIT WHEN remaining <= 0;
                      CONTINUE WHEN p.outstanding <= 0;
                      applied := LEAST(remaining, p.outstanding);
                      INSERT INTO "PenaltyWaivers" ("Id", "PenaltyId", "Amount", "Reason", "CreatedAt", "CreatedByUserId")
                      VALUES (gen_random_uuid(), p.id, applied, 'Migrated from ledger', w."TransactionDate"::timestamp AT TIME ZONE 'UTC', NULL);
                      remaining := remaining - applied;
                    END LOOP;
                  END LOOP;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RepaymentSchedules_CreatedByPaymentId",
                table: "RepaymentSchedules",
                column: "CreatedByPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_RepaymentSchedules_SupersededByPaymentId",
                table: "RepaymentSchedules",
                column: "SupersededByPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_LoanId_IdempotencyKey",
                table: "Payments",
                columns: new[] { "LoanId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_LoanId_PaymentDate",
                table: "Payments",
                columns: new[] { "LoanId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_PenaltyId",
                table: "PaymentAllocations",
                column: "PenaltyId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanTransactions_ReferenceId",
                table: "LoanTransactions",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanRecasts_PaymentId",
                table: "LoanRecasts",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReversals_PaymentId",
                table: "PaymentReversals",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_LoanId",
                table: "Penalties",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_ScheduleId",
                table: "Penalties",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyWaivers_PenaltyId",
                table: "PenaltyWaivers",
                column: "PenaltyId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_Penalties_PenaltyId",
                table: "PaymentAllocations",
                column: "PenaltyId",
                principalTable: "Penalties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RepaymentSchedules_Payments_CreatedByPaymentId",
                table: "RepaymentSchedules",
                column: "CreatedByPaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RepaymentSchedules_Payments_SupersededByPaymentId",
                table: "RepaymentSchedules",
                column: "SupersededByPaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_Penalties_PenaltyId",
                table: "PaymentAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_RepaymentSchedules_Payments_CreatedByPaymentId",
                table: "RepaymentSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_RepaymentSchedules_Payments_SupersededByPaymentId",
                table: "RepaymentSchedules");

            migrationBuilder.DropTable(
                name: "LoanRecasts");

            migrationBuilder.DropTable(
                name: "PaymentReversals");

            migrationBuilder.DropTable(
                name: "PenaltyWaivers");

            migrationBuilder.DropTable(
                name: "Penalties");

            migrationBuilder.DropIndex(
                name: "IX_RepaymentSchedules_CreatedByPaymentId",
                table: "RepaymentSchedules");

            migrationBuilder.DropIndex(
                name: "IX_RepaymentSchedules_SupersededByPaymentId",
                table: "RepaymentSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Payments_LoanId_IdempotencyKey",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_LoanId_PaymentDate",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_PenaltyId",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_LoanTransactions_ReferenceId",
                table: "LoanTransactions");

            migrationBuilder.DropColumn(
                name: "CreatedByPaymentId",
                table: "RepaymentSchedules");

            migrationBuilder.DropColumn(
                name: "SupersededByPaymentId",
                table: "RepaymentSchedules");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PenaltyAmount",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "PenaltyId",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "LoanTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_LoanId",
                table: "Payments",
                column: "LoanId");
        }
    }
}
