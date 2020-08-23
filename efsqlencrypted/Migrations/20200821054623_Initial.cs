using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EfSqlEncrypted.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            var sqlBuilder = new MigrationSqlBuilder();

            migrationBuilder.CreateTable(
                name: "Patients",
                columns: table => new
                {
                    PatientId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "varchar(max)", nullable: false),
                    SSN = table.Column<string>(type: "varchar(max)", nullable: false),
                    FirstName = table.Column<string>(type: "varchar(max)", nullable: true),
                    LastName = table.Column<string>(type: "varchar(max)", nullable: true),
                    BirthDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.PatientId);
                });

            sqlBuilder.CreateMasterKey();
            sqlBuilder.CreateEncryptionKey();
            migrationBuilder.Sql(sqlBuilder.PatientsEncryptionDrop());
            migrationBuilder.Sql(sqlBuilder.PatientsEncryptionAdd());
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Patients");
        }
    }
}
