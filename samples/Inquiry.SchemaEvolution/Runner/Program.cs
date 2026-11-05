using Catalog.Deployment;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;

// The sample needs a SQL Server it can create and drop databases and logins on, so it starts its own.
// Set INQUIRY_SQLSERVER_IMAGE to use another image.
var image = Environment.GetEnvironmentVariable("INQUIRY_SQLSERVER_IMAGE") is { Length: > 0 } configured
    ? configured
    : "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04@sha256:c1aa8afe9b06eab64c9774a4802dcd032205d1be785b1fd51e1c0151e7586b74";

// Testcontainers logs to standard output by default, which would mix with the sample's output.
await using var server = new MsSqlBuilder(image).WithLogger(NullLogger.Instance).Build();
await server.StartAsync();
await SchemaEvolution.RunAsync(server.GetConnectionString(), Console.Out);
