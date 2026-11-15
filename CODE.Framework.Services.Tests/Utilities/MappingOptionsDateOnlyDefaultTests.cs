using CODE.Framework.Fundamentals.Utilities;
using CODE.Framework.Services.Tests.Utilities.TestModels;
using Xunit;

namespace CODE.Framework.Services.Tests.Utilities;

[Collection(MappingOptionsStaticCollection.Name)]
public class MappingOptionsDateOnlyDefaultTests : IDisposable
{
	private readonly bool _originalDefault = MappingOptions.MapDateOnlyByDefault;

	public void Dispose() => MappingOptions.MapDateOnlyByDefault = _originalDefault;

	[Fact]
	public void MappingOptions_MapDateOnlyByDefaultTrue_NewOptionsOptIn()
	{
		MappingOptions.MapDateOnlyByDefault = true;
		var destination = new DateOnlyHolder();

		Mapper.Map(new DateTimeHolder { Value = new DateTime(2026, 11, 15) }, destination); // No options passed: uses the static default

		Assert.Multiple(
			() => Assert.True(new MappingOptions().MapDateOnly),
			() => Assert.Equal(new DateOnly(2026, 11, 15), destination.Value));
	}

	[Fact]
	public void MappingOptions_MapDateOnlyFalseWithDefaultTrue_SkipsDateOnly()
	{
		MappingOptions.MapDateOnlyByDefault = true;
		var destination = new DateOnlyHolder();

		Mapper.Map(new DateTimeHolder { Value = new DateTime(2026, 11, 15) }, destination, new MappingOptions { MapDateOnly = false });

		Assert.Equal(default, destination.Value);
	}
}