using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceReceiptQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_device_receipt_items_receipt_id",
                table: "device_receipt_items");

            migrationBuilder.DropIndex(
                name: "uq_device_receipt_items_barcode",
                table: "device_receipt_items");

            migrationBuilder.DropIndex(
                name: "uq_device_receipt_items_device_code",
                table: "device_receipt_items");

            migrationBuilder.AddColumn<int>(
                name: "quantity",
                table: "devices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Only completed receipts establish physical receipt of a device.
            migrationBuilder.Sql("""
                UPDATE devices AS device
                SET quantity = 1
                WHERE EXISTS (
                    SELECT 1 FROM device_receipt_items AS item
                    JOIN device_receipts AS receipt ON receipt.id = item.receipt_id
                    WHERE item.device_id = device.id AND receipt.status = 'COMPLETED'
                );
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_devices_quantity",
                table: "devices",
                sql: "quantity IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_device_receipt_items_barcode",
                table: "device_receipt_items",
                column: "barcode");

            migrationBuilder.CreateIndex(
                name: "IX_device_receipt_items_device_code",
                table: "device_receipt_items",
                column: "device_code");

            migrationBuilder.CreateIndex(
                name: "uq_device_receipt_items_receipt_barcode",
                table: "device_receipt_items",
                columns: new[] { "receipt_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_device_receipt_items_receipt_device_code",
                table: "device_receipt_items",
                columns: new[] { "receipt_id", "device_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_devices_quantity",
                table: "devices");

            migrationBuilder.DropIndex(
                name: "IX_device_receipt_items_barcode",
                table: "device_receipt_items");

            migrationBuilder.DropIndex(
                name: "IX_device_receipt_items_device_code",
                table: "device_receipt_items");

            migrationBuilder.DropIndex(
                name: "uq_device_receipt_items_receipt_barcode",
                table: "device_receipt_items");

            migrationBuilder.DropIndex(
                name: "uq_device_receipt_items_receipt_device_code",
                table: "device_receipt_items");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "devices");

            migrationBuilder.CreateIndex(
                name: "IX_device_receipt_items_receipt_id",
                table: "device_receipt_items",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "uq_device_receipt_items_barcode",
                table: "device_receipt_items",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_device_receipt_items_device_code",
                table: "device_receipt_items",
                column: "device_code",
                unique: true);
        }
    }
}
