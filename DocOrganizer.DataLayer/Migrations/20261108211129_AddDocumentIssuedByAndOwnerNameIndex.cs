using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocOrganizer.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentIssuedByAndOwnerNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IssuedBy",
                table: "Documents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Owners_FullName",
                table: "Owners",
                column: "FullName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Owners_FullName",
                table: "Owners");

            migrationBuilder.DropColumn(
                name: "IssuedBy",
                table: "Documents");
        }
    }
}
