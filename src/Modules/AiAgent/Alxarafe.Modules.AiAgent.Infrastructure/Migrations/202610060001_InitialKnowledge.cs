using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.AiAgent.Infrastructure.Migrations;

[DbContext(typeof(AiAgentDbContext))]
[Migration("202610060001_InitialKnowledge")]
public sealed class InitialKnowledge : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ai_knowledge",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Question = table.Column<string>(type: "text", nullable: false),
                Answer = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ai_knowledge", entry => entry.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("ai_knowledge");

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
            entity.ToTable("ai_knowledge");
        });
    }
}
