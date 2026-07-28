public interface IDocumentParserStrategy
{
    Task<string?> ParseAsync(byte[] fileBytes, string fileName, string mediaType, CancellationToken cancellationToken);
}
