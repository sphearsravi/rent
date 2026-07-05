using Microsoft.AspNetCore.Http;
using RentCollection.Application.Interfaces;

namespace RentCollection.Infrastructure.Services;

public class FileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public FileStorageService(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
    }

    /// <summary>
    /// Resolves a caller-supplied relative path to an absolute path under _rootPath
    /// and rejects any input that escapes the root (e.g. "..\\..\\appsettings.json").
    /// </summary>
    private string ResolveSafePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Path is required.", nameof(relativePath));

        // Normalize separators and reject inputs that already look absolute or
        // device-rooted; relative paths only.
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            throw new UnauthorizedAccessException("Absolute paths are not allowed.");

        var combined = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        var rootWithSep = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes the storage root.");

        return combined;
    }

    public async Task<string> SaveFileAsync(IFormFile file, string folderPath)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is empty or null.");

        var fullFolderPath = ResolveSafePath(folderPath);
        if (!Directory.Exists(fullFolderPath))
        {
            Directory.CreateDirectory(fullFolderPath);
        }

        // Filename is server-generated — never echo user input into the FS.
        var fileName = $"{Guid.NewGuid()}{GetFileExtension(file.FileName)}";
        var filePath = Path.Combine(fullFolderPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Path.Combine(folderPath, fileName);
    }

    public async Task<bool> DeleteFileAsync(string filePath)
    {
        var fullPath = ResolveSafePath(filePath);

        if (!File.Exists(fullPath))
            return false;

        await Task.Run(() => File.Delete(fullPath));
        return true;
    }

    public async Task<byte[]> GetFileAsync(string filePath)
    {
        var fullPath = ResolveSafePath(filePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {filePath}");

        return await File.ReadAllBytesAsync(fullPath);
    }

    public Task<bool> FileExistsAsync(string filePath)
    {
        try
        {
            var fullPath = ResolveSafePath(filePath);
            return Task.FromResult(File.Exists(fullPath));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    public string GetFileExtension(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant();
    }

    public bool IsValidFileType(string fileName, string[] allowedExtensions)
    {
        var extension = GetFileExtension(fileName);
        return allowedExtensions.Contains(extension);
    }

    public bool IsValidFileSize(long fileSize, long maxSizeInBytes)
    {
        return fileSize <= maxSizeInBytes;
    }
}
