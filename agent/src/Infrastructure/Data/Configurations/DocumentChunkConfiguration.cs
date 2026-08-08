using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunkEf>
{
    public void Configure(EntityTypeBuilder<DocumentChunkEf> builder)
    {
        builder.ToTable("Chunk");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.DocumentId).HasColumnName("documentId").IsRequired();
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.Seq).HasColumnName("chunk_index");
        builder.Property(x => x.StartAt).HasColumnName("start_at");
        builder.Property(x => x.EndAt).HasColumnName("end_at");
        builder.Property(x => x.ParentChunkId).HasColumnName("parent_chunk_id");
        builder.Property(x => x.ChunkType).HasColumnName("chunk_type").HasDefaultValue("text");
        builder.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1024)");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => x.ParentChunkId);
        builder.HasOne(x => x.Parent)
            .WithMany()
            .HasForeignKey(x => x.ParentChunkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}