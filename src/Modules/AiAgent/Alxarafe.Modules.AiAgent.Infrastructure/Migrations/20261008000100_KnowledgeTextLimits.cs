using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.AiAgent.Infrastructure.Migrations;

[DbContext(typeof(AiAgentDbContext))]
[Migration("20261008000100_KnowledgeTextLimits")]
public sealed class KnowledgeTextLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "CK_ai_knowledge_Question_Length",
            table: "ai_knowledge",
            sql: "char_length(\"Question\") BETWEEN 1 AND 1000");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ai_knowledge_Answer_Length",
            table: "ai_knowledge",
            sql: "char_length(\"Answer\") BETWEEN 1 AND 20000");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_ai_knowledge_Question_Length", "ai_knowledge");
        migrationBuilder.DropCheckConstraint("CK_ai_knowledge_Answer_Length", "ai_knowledge");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        modelBuilder.Entity("Alxarafe.Modules.AiAgent.Infrastructure.KnowledgeEntryRow", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            entity.Property<string>("Question").IsRequired().HasColumnType("text");
            entity.Property<string>("Answer").IsRequired().HasColumnType("text");
            entity.HasKey("Id");
            entity.ToTable("ai_knowledge", table =>
            {
                table.HasCheckConstraint("CK_ai_knowledge_Question_Length", "char_length(\"Question\") BETWEEN 1 AND 1000");
                table.HasCheckConstraint("CK_ai_knowledge_Answer_Length", "char_length(\"Answer\") BETWEEN 1 AND 20000");
            });
        });
    }
}
