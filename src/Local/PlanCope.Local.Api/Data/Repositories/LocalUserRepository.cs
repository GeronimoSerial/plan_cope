using Dapper;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data.Repositories;

public sealed class LocalUserRepository(ILocalSqliteConnectionFactory connectionFactory) : ILocalUserRepository
{
    public async Task<LocalUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, username, password_hash, role, active, last_login_at, created_at
            FROM local_users
            WHERE username = @Username
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LocalUserRow>(new CommandDefinition(sql, new { Username = username }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task<LocalUser?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, username, password_hash, role, active, last_login_at, created_at
            FROM local_users
            WHERE id = @Id
            LIMIT 1;
            """;

        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LocalUserRow>(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    private sealed class LocalUserRow
    {
        public string Id { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public long Active { get; init; }
        public string? LastLoginAt { get; init; }
        public string CreatedAt { get; init; } = string.Empty;

        public LocalUser ToDomain()
        {
            return new LocalUser(Id, Username, PasswordHash, Role, Active != 0, LastLoginAt, CreatedAt);
        }
    }
}
