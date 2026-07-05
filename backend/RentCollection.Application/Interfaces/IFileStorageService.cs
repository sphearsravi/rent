using Microsoft.AspNetCore.Http;

namespace RentCollection.Application.Interfaces;

/// <summary>
/// File storage service interface for handling file uploads
/// </summary>
public interface IFileStorageService
{
    Task<string> SaveFileAsync(IFormFile file, string folderPath);
    Task<bool> DeleteFileAsync(string filePath);
    Task<byte[]> GetFileAsync(string filePath);
    Task<bool> FileExistsAsync(string filePath);
    string GetFileExtension(string fileName);
    bool IsValidFileType(string fileName, string[] allowedExtensions);
    bool IsValidFileSize(long fileSize, long maxSizeInBytes);
}
