using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.AiAgent.Infrastructure.Migrations;

[DbContext(typeof(AiAgentDbContext))]
public sealed class AiAgentDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
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
