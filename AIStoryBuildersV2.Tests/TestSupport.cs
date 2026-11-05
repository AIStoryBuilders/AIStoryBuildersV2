using System.Security.Cryptography;
using System.Text;
using AIStoryBuildersV2.Shared.AI;
using AIStoryBuildersV2.Shared.Graph;
using AIStoryBuildersV2.Shared.Hosting;
using AIStoryBuildersV2.Shared.Models;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;
using Microsoft.Extensions.AI;

namespace AIStoryBuildersV2.Tests;

/// <summary>A unique temporary directory that is deleted on dispose (KGB).</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string relative) => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(Path)) return;
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class TestHost(string rootPath) : IHostCapabilities
{
    public bool CanToggleTheme => false;
    public bool CanOpenFolder => false;
    public bool IsBrowser { get; set; }
    public bool HasBackup => false;
    public bool DeletesAreRecoverable => false;
    public string RootDisplay => "Test root";
    public string RootPath => rootPath;
    public void OpenRootFolder() { }
    public bool SupportsSpeechToText => false;
}

/// <summary>Test-Data §2: deterministic 384-number vectors.</summary>
public static class FixtureVectors
{
    public static float[] Of(string text)
    {
        var seed = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0);
        var rng = new Random(seed);
        var v = new float[StoryFormat.VectorDimensions];
        for (var i = 0; i < v.Length; i++) v[i] = (float)(rng.NextDouble() * 2 - 1);
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        for (var i = 0; i < v.Length; i++) v[i] /= norm;
        return v;
    }

    public static string Format(string text) => StoryFormat.FormatVector(Of(text));
}

/// <summary>An embedding generator that returns <see cref="FixtureVectors.Of"/> (Test-Data §2).</summary>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<string> Inputs { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        Inputs.AddRange(list);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list.Select(v => new Embedding<float>(FixtureVectors.Of(v)))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>An <see cref="IStoryPersistence"/> that records MarkDirty calls (plan §10, MarkDirtyCoverageTests).</summary>
public sealed class RecordingPersistence : IStoryPersistence
{
    public List<string> Dirty { get; } = [];
    public Task OpenAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public void MarkDirty(string title) => Dirty.Add(title);
    public Task FlushAsync(string? title = null, CancellationToken ct = default) => Task.CompletedTask;
    public List<string> Deleted { get; } = [];
    public Task DeleteAsync(string title, CancellationToken ct = default) { Deleted.Add(title); return Task.CompletedTask; }
    public Task ReloadAsync(string title, CancellationToken ct = default) => Task.CompletedTask;
    public bool IsDirty(string title) => false;
    public Task<StorageUsage?> GetUsageAsync(CancellationToken ct = default) => Task.FromResult<StorageUsage?>(null);
    public event Action<StoryPersistenceEvent>? Changed { add { } remove { } }
}

/// <summary>An <see cref="IAppDataStore"/> in memory.</summary>
public sealed class MemoryAppDataStore : IAppDataStore
{
    public Dictionary<AppDataFile, string> Files { get; } = [];
    public Task<string?> ReadAsync(AppDataFile file, CancellationToken ct = default) => Task.FromResult(Files.TryGetValue(file, out var v) ? v : null);
    public Task WriteAsync(AppDataFile file, string content, CancellationToken ct = default) { Files[file] = content; return Task.CompletedTask; }
}

/// <summary>An <see cref="AIStoryBuildersService"/> over a temp stories root, with fixture vectors.</summary>
public sealed class ServiceFixture : IDisposable
{
    public ServiceFixture(bool writeBefore = true)
    {
        if (writeBefore) FixtureFactory.WriteBefore(Dir.Path);
        Log = new LogService(AppData) { SaveDelay = TimeSpan.FromMinutes(10) };
        Library = new CsvStoryLibraryStore(Dir.Path);
        var orchestrator = new OrchestratorMethods(Log, new DatabaseService(AppData), Embeddings);
        Service = new AIStoryBuildersService(new TestHost(Dir.Path), Library, Persistence, Log, Embeddings, orchestrator,
            new StoryGraphCache(), new GraphBuilder(), new AppMetadata { Version = StoryStorageConstants.StybldFormatVersion });
    }

    public TempDir Dir { get; } = new();
    public MemoryAppDataStore AppData { get; } = new();
    public LogService Log { get; }
    public CsvStoryLibraryStore Library { get; }
    public RecordingPersistence Persistence { get; } = new();
    public FakeEmbeddingGenerator Embeddings { get; } = new();
    public AIStoryBuildersService Service { get; }

    public string StoryDir => Dir.Combine(FixtureFactory.Title);

    public Story Story => new() { Title = FixtureFactory.Title };

    public Chapter Chapter(int n) => new() { ChapterName = "Chapter " + n, Sequence = n, Story = Story };

    public void Dispose() => Dir.Dispose();
}

/// <summary>Runs code under a culture and restores the previous one.</summary>
public sealed class CultureScope : IDisposable
{
    readonly System.Globalization.CultureInfo _culture, _ui;

    public CultureScope(string name)
    {
        _culture = System.Globalization.CultureInfo.CurrentCulture;
        _ui = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(name);
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(name);
    }

    public void Dispose()
    {
        System.Globalization.CultureInfo.CurrentCulture = _culture;
        System.Globalization.CultureInfo.CurrentUICulture = _ui;
    }
}
