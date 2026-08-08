using Microsoft.EntityFrameworkCore;

public interface IApplicationDbContext
{
    DbSet<ThreadEf> Threads { get; }
    DbSet<FormEf> Forms { get; }
    DbSet<FormSubmissionEf> FormSubmissions { get; }
    DbSet<DocumentEf> Documents { get; }
    DbSet<DocumentChunkEf> DocumentChunks { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}