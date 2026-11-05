namespace AIStoryBuildersV2.Shared.Knowledge;

public class KnowledgeException(string message) : Exception(message);

public sealed class StorageQuotaExceededException(long estimatedBytes) : KnowledgeException($"Browser storage is full (about {estimatedBytes:N0} bytes in use).")
{
    public long EstimatedBytes { get; } = estimatedBytes;
}
