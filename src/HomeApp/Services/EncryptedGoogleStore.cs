using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Util.Store;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace HomeApp.Services;

// OAuth data stays outside workspace.json, protected for the current Windows user.
internal sealed class EncryptedGoogleStore(string directory) : IDataStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string FileName<T>(string key) => System.IO.Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(T).FullName + ":" + key))) + ".bin");

    public async Task StoreAsync<T>(string key, T value)
    {
        var data = CryptographicBuffer.ConvertStringToBinary(JsonSerializer.Serialize(value), BinaryStringEncoding.Utf8);
        var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(data);
        CryptographicBuffer.CopyToByteArray(encrypted, out var bytes);
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            var file = FileName<T>(key);
            await File.WriteAllBytesAsync(file + ".tmp", bytes);
            File.Move(file + ".tmp", file, true);
        }
        finally { _gate.Release(); }
    }

    public async Task<T> GetAsync<T>(string key)
    {
        await _gate.WaitAsync();
        try
        {
            var file = FileName<T>(key);
            if (!File.Exists(file)) return default!;
            IBuffer data = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(file));
            var decrypted = await new DataProtectionProvider().UnprotectAsync(data);
            return JsonSerializer.Deserialize<T>(CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, decrypted))!;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync<T>(string key)
    {
        await _gate.WaitAsync();
        try { File.Delete(FileName<T>(key)); }
        finally { _gate.Release(); }
    }

    public Task ClearAsync() => throw new NotSupportedException("Delete individual account entries explicitly.");
}
