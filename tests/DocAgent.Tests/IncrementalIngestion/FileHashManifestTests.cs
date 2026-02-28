using DocAgent.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests.IncrementalIngestion;

public class FileHashManifestTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "FileHashManifestTests_" + Guid.NewGuid().ToString("N"));

    public FileHashManifestTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ComputeAsync_produces_expected_sha256_hex()
    {
        // SHA-256 of empty string is e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        var path = WriteFile("empty.txt", "");

        var hash = await FileHasher.ComputeAsync(path);

        hash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public async Task ComputeAsync_produces_lowercase_hex()
    {
        var path = WriteFile("test.txt", "hello");

        var hash = await FileHasher.ComputeAsync(path);

        hash.Should().MatchRegex("^[0-9a-f]{64}$", "SHA-256 hex should be 64 lowercase hex chars");
    }

    [Fact]
    public async Task Diff_with_null_previous_marks_all_files_as_added()
    {
        var path1 = WriteFile("a.cs", "class A {}");
        var path2 = WriteFile("b.cs", "class B {}");
        var manifest = await FileHasher.ComputeManifestAsync([path1, path2]);

        var diff = FileHasher.Diff(previous: null, current: manifest);

        diff.AddedFiles.Should().HaveCount(2);
        diff.ModifiedFiles.Should().BeEmpty();
        diff.RemovedFiles.Should().BeEmpty();
        diff.HasChanges.Should().BeTrue();
    }

    [Fact]
    public async Task Diff_detects_added_modified_and_removed_files()
    {
        var pathA = WriteFile("a.cs", "class A {}");
        var pathB = WriteFile("b.cs", "class B {}");
        var pathC = WriteFile("c.cs", "class C {}");

        var previous = await FileHasher.ComputeManifestAsync([pathA, pathB]);

        // Modify b, add c (new file), remove a
        File.WriteAllText(pathB, "class B { int x; }");
        var current = await FileHasher.ComputeManifestAsync([pathB, pathC]);

        var diff = FileHasher.Diff(previous, current);

        diff.AddedFiles.Should().Contain(pathC);
        diff.ModifiedFiles.Should().Contain(pathB);
        diff.RemovedFiles.Should().Contain(pathA);
        diff.HasChanges.Should().BeTrue();
    }

    [Fact]
    public async Task Diff_ChangedFiles_contains_added_and_modified_but_not_removed()
    {
        var pathA = WriteFile("a.cs", "class A {}");
        var pathB = WriteFile("b.cs", "class B {}");
        var pathC = WriteFile("c.cs", "class C {}");

        var previous = await FileHasher.ComputeManifestAsync([pathA, pathB]);

        File.WriteAllText(pathB, "class B { int x; }");
        var current = await FileHasher.ComputeManifestAsync([pathB, pathC]);

        var diff = FileHasher.Diff(previous, current);

        diff.ChangedFiles.Should().Contain(pathB);
        diff.ChangedFiles.Should().Contain(pathC);
        diff.ChangedFiles.Should().NotContain(pathA);
    }

    [Fact]
    public async Task Diff_with_no_changes_returns_empty_diff_with_HasChanges_false()
    {
        var pathA = WriteFile("a.cs", "class A {}");
        var manifest = await FileHasher.ComputeManifestAsync([pathA]);

        var diff = FileHasher.Diff(manifest, manifest);

        diff.AddedFiles.Should().BeEmpty();
        diff.ModifiedFiles.Should().BeEmpty();
        diff.RemovedFiles.Should().BeEmpty();
        diff.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrip_manifest()
    {
        var pathA = WriteFile("a.cs", "class A {}");
        var manifest = await FileHasher.ComputeManifestAsync([pathA]);

        var savePath = Path.Combine(_tempDir, "manifest.json");
        await FileHasher.SaveAsync(manifest, savePath);

        var loaded = await FileHasher.LoadAsync(savePath);

        loaded.Should().NotBeNull();
        loaded!.FileHashes.Should().BeEquivalentTo(manifest.FileHashes);
        loaded.SchemaVersion.Should().Be(manifest.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_returns_null_when_file_does_not_exist()
    {
        var nonExistentPath = Path.Combine(_tempDir, "nonexistent.json");

        var result = await FileHasher.LoadAsync(nonExistentPath);

        result.Should().BeNull();
    }
}
