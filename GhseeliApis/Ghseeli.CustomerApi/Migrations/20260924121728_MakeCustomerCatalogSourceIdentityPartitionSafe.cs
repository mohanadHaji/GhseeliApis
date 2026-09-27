using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.CustomerApi.Migrations
{
    /// <inheritdoc />
    public partial class MakeCustomerCatalogSourceIdentityPartitionSafe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CatalogProviders_SourceCompanyId",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropIndex(
                name: "IX_CatalogCategories_SourceCategoryId",
                schema: "dbo",
                table: "CatalogCategories");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProviders_SourceCompanyId_IsDemo",
                schema: "dbo",
                table: "CatalogProviders",
                columns: new[] { "SourceCompanyId", "IsDemo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCategories_ProviderId_SourceCategoryId",
                schema: "dbo",
                table: "CatalogCategories",
                columns: new[] { "ProviderId", "SourceCategoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT [SourceCompanyId]
                    FROM [dbo].[CatalogProviders]
                    GROUP BY [SourceCompanyId]
                    HAVING COUNT(*) > 1
                )
                OR EXISTS
                (
                    SELECT [SourceCategoryId]
                    FROM [dbo].[CatalogCategories]
                    GROUP BY [SourceCategoryId]
                    HAVING COUNT(*) > 1
                )
                BEGIN
                    THROW 51001, 'Cannot downgrade customer catalog source identity: duplicate SourceCompanyId or SourceCategoryId values exist; global unique indexes cannot be restored.', 1;
                END
                """);

            migrationBuilder.DropIndex(
                name: "IX_CatalogProviders_SourceCompanyId_IsDemo",
                schema: "dbo",
                table: "CatalogProviders");

            migrationBuilder.DropIndex(
                name: "IX_CatalogCategories_ProviderId_SourceCategoryId",
                schema: "dbo",
                table: "CatalogCategories");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProviders_SourceCompanyId",
                schema: "dbo",
                table: "CatalogProviders",
                column: "SourceCompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCategories_SourceCategoryId",
                schema: "dbo",
                table: "CatalogCategories",
                column: "SourceCategoryId",
                unique: true);
        }
    }
}
