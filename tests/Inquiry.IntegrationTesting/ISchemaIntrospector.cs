using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Inquiry.IntegrationTesting;

/// <summary>Reads the live catalog of an already-created schema into a <see cref="SchemaSnapshot"/>.</summary>
public interface ISchemaIntrospector
{
    Task<SchemaSnapshot> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default);
}
