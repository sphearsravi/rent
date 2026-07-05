using System.Security.Cryptography;
using System.Text;

namespace RentCollection.Infrastructure.Services;

/// <summary>
/// Utility for KNET payment gateway encryption/decryption.
/// Ported from https://github.com/kuwaitdevs/knet-dotnet-core-integration
/// Uses AES-128-CBC with the Terminal Resource Key provided by KNET.
/// </summary>
public class KnetUtil
{
    /// <summary>
    /// Generates the full KNET payment gateway redirect URL with encrypted transaction data.
    /// </summary>
    public string GenerateKnetUrl(
        string tranportalId,
        string tranportalPwd,
        string terminalResourceKey,
        string paymentApiUrl,
        decimal amount,
        string trackId,
        string responseUrl,
        string errorUrl,
        string lang = "en",
        string? udf1 = null,
        string? udf2 = null,
        string? udf3 = null,
        string? udf4 = null,
        string? udf5 = null)
    {
        // KNET expects language codes: "USA" for English, "ARA" for Arabic
        var langId = lang?.ToLower() switch
        {
            "ar" or "ara" => "ARA",
            _ => "USA"
        };

        // Build cleartext parameter string (KWD uses 3 decimal places, currency code 414)
        var clearTextParams =
            $"id={tranportalId}&password={tranportalPwd}&action=1&" +
            $"langid={langId}&currencycode=414&amt={amount:0.000}&" +
            $"responseURL={responseUrl}&errorURL={errorUrl}&" +
            $"trackid={trackId}&" +
            $"udf1={udf1 ?? ""}&udf2={udf2 ?? ""}&udf3={udf3 ?? ""}&udf4={udf4 ?? ""}&udf5={udf5 ?? ""}&";

        // Encrypt the parameters
        var encryptedUrlParams = EncryptUrlParams(clearTextParams, terminalResourceKey);

        // Build the transaction data with unencrypted routing info
        var transactionData =
            $"{encryptedUrlParams}&tranportalId={tranportalId}&" +
            $"responseURL={responseUrl}&errorURL={errorUrl}";

        // Build the full redirect URL
        return $"{paymentApiUrl}&trandata={transactionData}";
    }

    /// <summary>
    /// Encrypts URL parameters using AES-128-CBC with the terminal resource key.
    /// Returns uppercase hex of the raw encrypted bytes (matching KNET reference implementation).
    /// </summary>
    public string EncryptUrlParams(string clearText, string terminalResourceKey)
    {
        using var aes = CreateAes(terminalResourceKey);
        using var encryptor = aes.CreateEncryptor();
        var inputBytes = Encoding.UTF8.GetBytes(clearText);
        var encryptedBytes = encryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);

        // Convert raw encrypted bytes to uppercase hex (matching KNET reference)
        return BitConverter.ToString(encryptedBytes).Replace("-", "");
    }

    /// <summary>
    /// Decrypts URL parameters received from KNET callback.
    /// Input is uppercase hex of raw encrypted bytes.
    /// </summary>
    public string DecryptUrlParams(string encryptedData, string terminalResourceKey)
    {
        // Convert hex string directly to raw encrypted bytes
        var encryptedBytes = Convert.FromHexString(encryptedData);

        using var aes = CreateAes(terminalResourceKey);
        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    /// <summary>
    /// Parses a KNET response parameter string into a dictionary.
    /// Format: "key1=value1&key2=value2&..."
    /// </summary>
    public Dictionary<string, string> ParseResponseParams(string decryptedParams)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pairs = decryptedParams.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = pair[..eqIndex].Trim();
                var value = eqIndex < pair.Length - 1 ? pair[(eqIndex + 1)..].Trim() : "";
                result[key] = value;
            }
        }

        return result;
    }

    private static Aes CreateAes(string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = keyBytes; // KNET uses key as IV
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }

}
