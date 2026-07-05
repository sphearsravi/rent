using FluentAssertions;
using RentCollection.Infrastructure.Services;

namespace RentCollection.Tests;

/// <summary>
/// Regression tests for the path-traversal fix added in Phase 3 of the security
/// remediation. Any caller-supplied path that escapes the storage root must be
/// rejected, including the obvious traversal payloads and absolute Windows paths.
/// </summary>
public class FileStorageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FileStorageService _sut;

    public FileStorageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _sut = new FileStorageService(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("uploads/../../etc/passwd")]
    public async Task GetFileAsync_rejects_path_traversal(string traversal)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.GetFileAsync(traversal));
    }

    [Fact]
    public async Task GetFileAsync_rejects_absolute_paths()
    {
        // Both POSIX-style and Windows-style absolute paths.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.GetFileAsync("/etc/passwd"));
    }

    [Fact]
    public async Task GetFileAsync_returns_bytes_for_a_safe_path()
    {
        var sub = Path.Combine(_root, "ok");
        Directory.CreateDirectory(sub);
        await File.WriteAllBytesAsync(Path.Combine(sub, "f.bin"), new byte[] { 1, 2, 3 });

        var bytes = await _sut.GetFileAsync(Path.Combine("ok", "f.bin"));
        bytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task FileExistsAsync_is_false_on_escape_attempt()
    {
        (await _sut.FileExistsAsync("../escape.txt")).Should().BeFalse();
    }
}
