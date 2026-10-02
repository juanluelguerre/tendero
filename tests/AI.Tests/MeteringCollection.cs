using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>The metering instruments are static: tests that listen to them run alone.</summary>
[CollectionDefinition(nameof(MeteringChatClientTests), DisableParallelization = true)]
public sealed class MeteringCollection;
