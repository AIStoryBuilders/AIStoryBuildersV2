#nullable disable
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Shared.AI;

/// <summary>
/// V1's AI orchestration (AI/OrchestratorMethods*.cs). T2a ports the non-AI helpers; the AI methods arrive in T7a/T7b.
/// </summary>
public partial class OrchestratorMethods
{
    public event EventHandler<ReadTextEventArgs> ReadTextEvent;

    public LogService LogService { get; }
    public DatabaseService DatabaseService { get; }

    readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public OrchestratorMethods(LogService logService, DatabaseService databaseService, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        LogService = logService;
        DatabaseService = databaseService;
        _embeddingGenerator = embeddingGenerator;
    }

    // Memory and Vectors

    /// <summary>
    /// V1's embedding helper. The vector is written with the invariant culture (D30).
    /// With <paramref name="Combine"/>, returns "{text}|{vector}"; callers pass text already cleaned for the file (D32).
    /// </summary>
    public async Task<string> GetVectorEmbedding(string EmbeddingContent, bool Combine)
    {
        var vector = await GetVectorEmbeddingAsFloats(EmbeddingContent);
        var vectorsToSave = StoryFormat.FormatVector(vector);
        return Combine ? EmbeddingContent + "|" + vectorsToSave : vectorsToSave;
    }

    public async Task<float[]> GetVectorEmbeddingAsFloats(string EmbeddingContent)
    {
        var embeddings = await _embeddingGenerator.GenerateAsync([EmbeddingContent ?? ""]);
        return embeddings[0].Vector.ToArray();
    }

    public async Task<float[][]> GetBatchEmbeddings(string[] texts)
    {
        var embeddings = await _embeddingGenerator.GenerateAsync(texts);
        return embeddings.Select(e => e.Vector.ToArray()).ToArray();
    }

    // Utility Methods

    public float CosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1 is null || vector2 is null || vector1.Length != vector2.Length) return 0;
        float dotProduct = 0, magnitude1 = 0, magnitude2 = 0;
        for (var i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }
        var denominator = MathF.Sqrt(magnitude1) * MathF.Sqrt(magnitude2);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }

    public static string TrimToMaxWords(string input, int maxWords = 500)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var words = input.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? input : string.Join(" ", words.Take(maxWords));
    }

    public static string TrimInnerSpaces(string input) =>
        string.IsNullOrEmpty(input) ? input : Regex.Replace(input, @"\s{2,}", " ");

    /// <summary>V1's SanitizeFileName (invalid file-name characters and '|' removed, not trimmed).</summary>
    public string SanitizeFileName(string input) => StoryFormat.SanitizeFileName(input);

    /// <summary>A paragraph's character list. Unlike V1, names are trimmed and empty entries dropped (Test-Data §4).</summary>
    public static List<string> ParseStringToList(string input) => StoryFormat.ParseCharacterList(input);

    protected void RaiseReadText(string message, int displayLength) =>
        ReadTextEvent?.Invoke(this, new ReadTextEventArgs(message, displayLength));

    public class ReadTextEventArgs : EventArgs
    {
        public string Message { get; set; }
        public int DisplayLength { get; set; }

        public ReadTextEventArgs(string message, int display_length)
        {
            Message = message;
            DisplayLength = display_length;
        }
    }
}
