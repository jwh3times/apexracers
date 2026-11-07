using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ApexRacers.Data.IndependentJournal;

/// <summary>Turns identifiers into keyed-hash blob names and records into authenticated
/// ciphertext. A payload is bound to the name it was written under, so a record moved to another
/// name fails authentication instead of reading as a different fact.</summary>
internal sealed class JournalCodec
{
    private const byte FormatVersion = 1;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private readonly byte[] naming;
    private readonly byte[] sealing;

    public JournalCodec(byte[] naming, byte[] sealing)
    {
        if (naming.Length != 32 || sealing.Length != 32 || naming.AsSpan().SequenceEqual(sealing))
            throw new ArgumentException("The naming and sealing materials must be two different 32-byte values.");
        this.naming = [.. naming];
        this.sealing = [.. sealing];
    }

    public string NamingFingerprint => Fingerprint("naming", naming);
    public string SealingFingerprint => Fingerprint("sealing", sealing);

    /// <summary>A stable, non-reversible name segment for one identifier in one domain.</summary>
    public string Segment(string domain, string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(naming, Encoding.UTF8.GetBytes($"{domain}:{value}")));

    public byte[] Seal<T>(string name, T record)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(record);
        var output = new byte[1 + NonceBytes + TagBytes + plaintext.Length];
        output[0] = FormatVersion;
        var nonce = output.AsSpan(1, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(sealing, TagBytes);
        aes.Encrypt(nonce, plaintext, output.AsSpan(1 + NonceBytes + TagBytes), output.AsSpan(1 + NonceBytes, TagBytes),
            Encoding.UTF8.GetBytes(name));
        return output;
    }

    /// <summary>Throws <see cref="CryptographicException"/> for a tampered, moved or foreign record.</summary>
    public T Open<T>(string name, ReadOnlySpan<byte> sealedRecord)
    {
        if (sealedRecord.Length < 1 + NonceBytes + TagBytes || sealedRecord[0] != FormatVersion)
            throw new CryptographicException("Unrecognized journal record format.");
        var plaintext = new byte[sealedRecord.Length - 1 - NonceBytes - TagBytes];
        using var aes = new AesGcm(sealing, TagBytes);
        aes.Decrypt(sealedRecord.Slice(1, NonceBytes), sealedRecord[(1 + NonceBytes + TagBytes)..],
            sealedRecord.Slice(1 + NonceBytes, TagBytes), plaintext, Encoding.UTF8.GetBytes(name));
        return JsonSerializer.Deserialize<T>(plaintext) ?? throw new CryptographicException("Empty journal record.");
    }

    private static string Fingerprint(string purpose, byte[] material) =>
        Convert.ToHexStringLower(SHA256.HashData([.. Encoding.UTF8.GetBytes($"apexracers-journal-{purpose}:"), .. material]));
}
