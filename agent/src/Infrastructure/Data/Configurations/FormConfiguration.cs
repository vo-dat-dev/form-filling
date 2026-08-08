using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class FormConfiguration : IEntityTypeConfiguration<FormEf>
{
    public void Configure(EntityTypeBuilder<FormEf> builder)
    {
        builder.ToTable("Form");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Title).HasColumnName("title").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.Fields).HasColumnName("fields").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1024)");
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.Property(x => x.UpdatedAt).HasColumnName("updatedAt");
    }
}