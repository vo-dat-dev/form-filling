using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ThreadConfiguration : IEntityTypeConfiguration<ThreadEf>
{
    public void Configure(EntityTypeBuilder<ThreadEf> builder)
    {
        builder.ToTable("Thread");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.AgentId).HasColumnName("agentId").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasDefaultValue("New Conversation");
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
        builder.HasIndex(x => x.AgentId);
    }
}