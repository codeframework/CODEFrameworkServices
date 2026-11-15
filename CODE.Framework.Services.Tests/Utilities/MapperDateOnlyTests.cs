using CODE.Framework.Fundamentals.Utilities;
using CODE.Framework.Services.Tests.Utilities.TestModels;
using Xunit;

namespace CODE.Framework.Services.Tests.Utilities;

public class MapperDateOnlyTests
{
	private static readonly DateOnly ExpectedDate = new(2026, 11, 15);
	private static readonly DateTime ExpectedMidnight = new(2026, 11, 15);
	private static readonly DateOnly PresetDate = new(2020, 1, 1);

	[Fact]
	public void MapObjects_DateOnlyToDateTime_CopiesMidnight()
	{
		var destination = new DateTimeHolder();

		Mapper.Map(new DateOnlyHolder { Value = ExpectedDate }, destination, OptIn());

		Assert.Multiple(
			() => Assert.Equal(ExpectedMidnight, destination.Value),
			() => Assert.Equal(DateTimeKind.Unspecified, destination.Value.Kind));
	}

	[Fact]
	public void MapObjects_DateOnlyToNullableDateTime_CopiesMidnight()
	{
		var destination = new NullableDateTimeHolder();

		Mapper.Map(new DateOnlyHolder { Value = ExpectedDate }, destination, OptIn());

		Assert.Equal<DateTime?>(ExpectedMidnight, destination.Value);
	}

	[Fact]
	public void MapObjects_DateTimeToDateOnly_CopiesDate()
	{
		var destination = new DateOnlyHolder();

		Mapper.Map(new DateTimeHolder { Value = ExpectedMidnight }, destination, OptIn());

		Assert.Equal(ExpectedDate, destination.Value);
	}

	[Fact]
	public void MapObjects_DateTimeWithTime_DropsTime()
	{
		var destination = new DateOnlyHolder();

		Mapper.Map(new DateTimeHolder { Value = new DateTime(2026, 11, 15, 14, 30, 45) }, destination, OptIn());

		Assert.Equal(ExpectedDate, destination.Value);
	}

	[Fact]
	public void MapObjects_ExcludedField_NotMapped()
	{
		var source = new ContractDates { Name = "Test", StartDate = ExpectedMidnight, EndDate = ExpectedMidnight };
		var destination = new ModelDates();
		var options = OptIn();
		options.ExcludedFields = new List<string> { nameof(ContractDates.StartDate) };

		Mapper.Map(source, destination, options);

		Assert.Multiple(
			() => Assert.Equal(default, destination.StartDate),
			() => Assert.Equal<DateOnly?>(ExpectedDate, destination.EndDate));
	}

	[Fact]
	public void MapObjects_LogMappedMembers_LogsDateProperties()
	{
		var source = new ContractDates { Name = "Test", StartDate = ExpectedMidnight, EndDate = ExpectedMidnight };
		var options = OptIn();
		options.LogMappedMembers = true;

		Mapper.Map(source, new ModelDates(), options);

		Assert.Multiple(
			() => Assert.Contains(nameof(ModelDates.StartDate), options.MappedDestinationProperties),
			() => Assert.Contains(nameof(ModelDates.EndDate), options.MappedDestinationProperties));
	}

	[Fact]
	public void MapObjects_MapDateOnlyOff_SkipsDateOnly()
	{
		var destination = new DateOnlyHolder();

		Mapper.Map(new DateTimeHolder { Value = ExpectedMidnight }, destination, new MappingOptions { MapDateOnly = false });

		Assert.Equal(default, destination.Value);
	}

	[Fact]
	public void MapObjects_NullableDateOnlyToNullableDateTime_CopiesValue()
	{
		var destination = new NullableDateTimeHolder();

		Mapper.Map(new NullableDateOnlyHolder { Value = ExpectedDate }, destination, OptIn());

		Assert.Equal<DateTime?>(ExpectedMidnight, destination.Value);
	}

	[Fact]
	public void MapObjects_NullableDateTimeToDateOnly_CopiesValue()
	{
		var destination = new DateOnlyHolder();

		Mapper.Map(new NullableDateTimeHolder { Value = ExpectedMidnight }, destination, OptIn());

		Assert.Equal(ExpectedDate, destination.Value);
	}

	[Fact]
	public void MapObjects_NullableDateTimeToNullableDateOnly_CopiesValue()
	{
		var destination = new NullableDateOnlyHolder();

		Mapper.Map(new NullableDateTimeHolder { Value = ExpectedMidnight }, destination, OptIn());

		Assert.Equal<DateOnly?>(ExpectedDate, destination.Value);
	}

	[Fact]
	public void MapObjects_NullDateTimeToDateOnly_LeavesDestinationUnchanged()
	{
		var destination = new DateOnlyHolder { Value = PresetDate };

		Mapper.Map(new NullableDateTimeHolder { Value = null }, destination, OptIn());

		Assert.Equal(PresetDate, destination.Value);
	}

	[Fact]
	public void MapObjects_NullDateTimeToNullableDateOnly_SetsNull()
	{
		var destination = new NullableDateOnlyHolder { Value = PresetDate };

		Mapper.Map(new NullableDateTimeHolder { Value = null }, destination, OptIn());

		Assert.Null(destination.Value);
	}

	[Fact]
	public void MapObjects_SameTypeProperty_StillMaps()
	{
		var destination = new ModelDates();

		Mapper.Map(new ContractDates { Name = "Test" }, destination, OptIn());

		Assert.Equal("Test", destination.Name);
	}

	private static MappingOptions OptIn() => new() { MapDateOnly = true };
}