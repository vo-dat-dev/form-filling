using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class DocumentConfiguration : IEntityTypeConfiguration<DocumentEf>
{
    public void Configure(EntityTypeBuilder<DocumentEf> builder)
    {
        builder.ToTable("Document");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.FileName).HasColumnName("fileName").IsRequired();
        builder.Property(x => x.MediaType).HasColumnName("mediaType");
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
        builder.HasMany(x => x.Chunks)
            .WithOne(c => c.Document)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}