using System.Data;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface ISqlConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
