using System.Security.Cryptography;
using System.Text;

namespace PlanCope.Local.Api.Services;

public interface IStudentResolutionTokenService
{
    string CreateToken();

    string HashToken(string token);
}

public sealed class StudentResolutionTokenService : IStudentResolutionTokenService
{
    public string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');

    public string HashToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Resolution token is required.", nameof(token));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
