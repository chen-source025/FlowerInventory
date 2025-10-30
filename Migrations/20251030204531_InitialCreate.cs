using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FlowerInventory.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Flowers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Variety = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    ABCClass = table.Column<string>(type: "text", nullable: false),
                    ShelfLifeDays = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    SeasonalFactor = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    InspectionPassRate = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: false),
                    LeadTimeWeeks = table.Column<int>(type: "integer", nullable: false),
                    SupplierDeliveriesPerWeek = table.Column<int>(type: "integer", nullable: false),
                    ReplenishCycleWeeks = table.Column<int>(type: "integer", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flowers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Batches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FlowerId = table.Column<int>(type: "integer", nullable: false),
                    BatchNo = table.Column<string>(type: "text", nullable: true),
                    QuantityReceived = table.Column<int>(type: "integer", nullable: false),
                    QuantityPassed = table.Column<int>(type: "integer", nullable: false),
                    ReceivedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InspectionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Batches_Flowers_FlowerId",
                        column: x => x.FlowerId,
                        principalTable: "Flowers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FlowerId = table.Column<int>(type: "integer", nullable: false),
                    BatchId = table.Column<int>(type: "integer", nullable: true),
                    ChangeQty = table.Column<int>(type: "integer", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transactions_Flowers_FlowerId",
                        column: x => x.FlowerId,
                        principalTable: "Flowers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_BatchNo",
                table: "Batches",
                column: "BatchNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ExpiryDate",
                table: "Batches",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_FlowerId",
                table: "Batches",
                column: "FlowerId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ReceivedDate",
                table: "Batches",
                column: "ReceivedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_Status",
                table: "Batches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Flowers_ABCClass",
                table: "Flowers",
                column: "ABCClass");

            migrationBuilder.CreateIndex(
                name: "IX_Flowers_Category",
                table: "Flowers",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Flowers_Name",
                table: "Flowers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BatchId",
                table: "Transactions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_FlowerId_TransactionDate",
                table: "Transactions",
                columns: new[] { "FlowerId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactionDate",
                table: "Transactions",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactionType",
                table: "Transactions",
                column: "TransactionType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Batches");

            migrationBuilder.DropTable(
                name: "Flowers");
        }
    }
}
