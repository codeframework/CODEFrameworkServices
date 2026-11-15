using Xunit;

namespace CODE.Framework.Services.Tests.Utilities;

/// <summary>
/// Runs tests that change MappingOptions static state on their own, so they don't affect tests running in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class MappingOptionsStaticCollection
{
	public const string Name = "MappingOptions static state";
}