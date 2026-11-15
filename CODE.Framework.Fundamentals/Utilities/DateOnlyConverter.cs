using System;
using System.Reflection;

#nullable enable

namespace CODE.Framework.Fundamentals.Utilities
{
	/// <summary>
	/// Converts between DateTime and System.DateOnly by reflection, because DateOnly isn't available in netstandard2.0.
	/// </summary>
	internal static class DateOnlyConverter
	{
		private const string DateOnlyTypeName = "System.DateOnly";

		private static PropertyInfo? _dayNumberProperty;
		private static MethodInfo? _fromDateTimeMethod;

		/// <summary>
		/// Returns the type itself, or its underlying type if it is nullable.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <returns>The non-nullable type.</returns>
		public static Type GetNonNullableType(Type type) => Nullable.GetUnderlyingType(type) ?? type;

		/// <summary>
		/// Returns true if the type is DateOnly or DateOnly?.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <returns>True if DateOnly, otherwise false.</returns>
		public static bool IsDateOnly(Type type) => GetNonNullableType(type).FullName == DateOnlyTypeName;

		/// <summary>
		/// Returns true if the type is DateTime or DateTime?.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <returns>True if DateTime, otherwise false.</returns>
		public static bool IsDateTime(Type type) => GetNonNullableType(type) == typeof(DateTime);

		/// <summary>
		/// Converts a DateTime to a DateOnly, dropping the time.
		/// </summary>
		/// <param name="value">The DateTime.</param>
		/// <param name="dateOnlyType">The non-nullable DateOnly type.</param>
		/// <returns>A boxed DateOnly.</returns>
		public static object ToDateOnly(DateTime value, Type dateOnlyType)
		{
			_fromDateTimeMethod ??= dateOnlyType.GetMethod("FromDateTime", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(DateTime) }, null)
				?? throw new MissingMemberException(DateOnlyTypeName, "FromDateTime");
			return _fromDateTimeMethod.Invoke(null, new object[] { value })!;
		}

		/// <summary>
		/// Converts a DateOnly to a DateTime at midnight, with an unspecified kind.
		/// </summary>
		/// <param name="dateOnly">A boxed DateOnly.</param>
		/// <returns>The DateTime.</returns>
		public static DateTime ToDateTime(object dateOnly)
		{
			// DayNumber counts days since 0001-01-01, so this matches DateOnly.ToDateTime(TimeOnly.MinValue)
			_dayNumberProperty ??= dateOnly.GetType().GetProperty("DayNumber", BindingFlags.Public | BindingFlags.Instance)
				?? throw new MissingMemberException(DateOnlyTypeName, "DayNumber");
			var dayNumber = (int)_dayNumberProperty.GetValue(dateOnly, null)!;
			return new DateTime(dayNumber * TimeSpan.TicksPerDay);
		}
	}
}