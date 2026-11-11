using Xunit;

namespace ElGuerre.Tendero.Tests.Ucp;

/// <summary>
/// The collection <see cref="UcpSchemas.Collection"/> names, declared here and not
/// beside the loader: <c>UcpSchemas.cs</c> is also compiled into Api.Tests and
/// Integration.Tests, where no class joins the collection, and xUnit1073 refuses
/// a definition nothing uses.
/// </summary>
[CollectionDefinition(UcpSchemas.Collection)]
public sealed class UcpSchemasCollection;
