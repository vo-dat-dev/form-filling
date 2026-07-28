internal sealed class MinerUCloudParserStrategy : IDocumentParserStrategy
{
    private readonly MinerUCloudService _inner;

    public MinerUCloudParserStrategy(MinerUCloudService inner)
    {
        _inner = inner;
    }

    public Task<string?> ParseAsync(byte[] fileBytes, string fileName, string mediaType, CancellationToken cancellationToken)
        => _inner.ParseAsync(fileBytes, fileName, mediaType, cancellationToken);
}
