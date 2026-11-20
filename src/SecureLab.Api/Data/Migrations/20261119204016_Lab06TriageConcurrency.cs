using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureLab.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Lab06TriageConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {


            migrationBuilder.CreateIndex(
                name: "ux_incident_status_history_triaged",
                table: "incident_status_history",
                column: "incident_id",
                unique: true,
                filter: "new_status = 'Triaged'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_incident_status_history_triaged",
                table: "incident_status_history");


        }
    }
}
