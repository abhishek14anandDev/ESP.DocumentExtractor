using System.Data;
using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.Data;

public sealed class SqlConnectionFactory(IOptions<SqlOptions> options) : ISqlConnectionFactory
{
    public async Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
