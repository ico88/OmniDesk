using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniDesk.Api.Data;

#nullable disable

namespace OmniDesk.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261020110000_KnowledgeFullTextSearch")]
public sealed class KnowledgeFullTextSearch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "KnowledgeArticles"
            ADD COLUMN "SearchVector" tsvector GENERATED ALWAYS AS (
                setweight(to_tsvector('simple', coalesce("Title", '')), 'A') ||
                setweight(to_tsvector('simple', coalesce("Summary", '')), 'B') ||
                setweight(to_tsvector('simple', coalesce("Body", '')), 'C')
            ) STORED;
            CREATE INDEX "IX_KnowledgeArticles_SearchVector"
            ON "KnowledgeArticles" USING GIN ("SearchVector");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX "IX_KnowledgeArticles_SearchVector";
            ALTER TABLE "KnowledgeArticles" DROP COLUMN "SearchVector";
            """);
    }
}
