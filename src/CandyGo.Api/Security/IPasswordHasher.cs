namespace CandyGo.Api.Security;

public interface IPasswordHasher
{
    void CreateHash(string password, out byte[] hash, out byte[] salt);
    bool Verify(string password, byte[] expectedHash, byte[] salt);
}
