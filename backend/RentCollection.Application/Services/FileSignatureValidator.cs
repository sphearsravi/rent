using Microsoft.AspNetCore.Http;

namespace RentCollection.Application.Services;

/// <summary>
/// SECURITY (audit H11): upload validation previously trusted the client-supplied
/// filename extension and Content-Type, so a renamed script or a polyglot passed
/// the "images only" gate. This inspects the actual leading bytes (magic number)
/// and confirms the file really is one of the allowed types (JPEG/PNG/GIF/WebP/
/// PDF) before it is stored — the content-type cannot be spoofed past this.
/// </summary>
public static class FileSignatureValidator
{
    private static readonly (string Name, byte[] Sig)[] Signatures =
    {
        ("jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
        ("png",  new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ("gif",  new byte[] { 0x47, 0x49, 0x46, 0x38 }),                       // GIF8
        ("pdf",  new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }),                 // %PDF-
        ("riff", new byte[] { 0x52, 0x49, 0x46, 0x46 }),                       // WebP container
    };

    /// <summary>True if the file's real content is an allowed image or PDF.</summary>
    public static bool IsAllowed(IFormFile file)
    {
        if (file == null || file.Length == 0) return false;

        var header = new byte[16];
        int read;
        using (var stream = file.OpenReadStream())
        {
            read = stream.Read(header, 0, header.Length);
        }
        if (read < 4) return false;

        foreach (var (name, sig) in Signatures)
        {
            if (StartsWith(header, read, sig))
            {
                if (name == "riff")
                    return read >= 12 && header[8] == 0x57 && header[9] == 0x45
                        && header[10] == 0x42 && header[11] == 0x50;           // "WEBP"
                return true;
            }
        }

        // HEIC/HEIF (iPhone photos) and the ISO-BMFF family carry "ftyp" at
        // offset 4. Accept these so legitimate iOS uploads aren't rejected.
        if (read >= 12 && header[4] == 0x66 && header[5] == 0x74
            && header[6] == 0x79 && header[7] == 0x70)                          // "ftyp"
            return true;

        return false;
    }

    private static bool StartsWith(byte[] data, int dataLen, byte[] prefix)
    {
        if (dataLen < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
            if (data[i] != prefix[i]) return false;
        return true;
    }
}
