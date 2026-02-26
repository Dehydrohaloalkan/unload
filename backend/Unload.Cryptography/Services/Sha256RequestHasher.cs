using System.Security.Cryptography;
using System.Text;
using Unload.Core;

namespace Unload.Cryptography;

public sealed class Sha256RequestHasher : IRequestHasher
{
    public string ComputeHash(string value)
    {
        var input = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
