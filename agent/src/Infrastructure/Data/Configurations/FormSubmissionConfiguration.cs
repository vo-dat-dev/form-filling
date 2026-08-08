using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmissionEf>
{
    public void Configure(EntityTypeBuilder<FormSubmissionEf> builder)
    {
        builder.ToTable("FormSubmission");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.FormId).HasColumnName("formId").IsRequired();
        builder.Property(x => x.Data).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("createdAt");
        builder.HasOne(x => x.Form)
            .WithMany(f => f.Submissions)
            .HasForeignKey(x => x.FormId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}