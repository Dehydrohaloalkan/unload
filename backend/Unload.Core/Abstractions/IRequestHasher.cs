namespace Unload.Core;

public interface IRequestHasher
{
    string ComputeHash(string value);
}
