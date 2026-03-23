using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoViewer.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaFileIds = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ZipFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    ZipFileName = table.Column<string>(type: "TEXT", nullable: true),
                    ZipFileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    FullPath = table.Column<string>(type: "TEXT", nullable: false),
                    Extension = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256Checksum = table.Column<string>(type: "TEXT", nullable: true),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    TakenDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IndexedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ThumbnailPath = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "PasswordHash", "Role", "Username" },
                values: new object[] { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "$2a$11$GYgaJz03GOZP9z5aHRJeluIV1WpN1in0O34SNsXPdCYKCLbXZ1CB2", 1, "admin" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_FileName",
                table: "MediaFiles",
                column: "FileName");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_IsDeleted",
                table: "MediaFiles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_MediaType",
                table: "MediaFiles",
                column: "MediaType");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_RelativePath",
                table: "MediaFiles",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_Sha256Checksum",
                table: "MediaFiles",
                column: "Sha256Checksum");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_TakenDate",
                table: "MediaFiles",
                column: "TakenDate");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadRequests");

            migrationBuilder.DropTable(
                name: "MediaFiles");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
