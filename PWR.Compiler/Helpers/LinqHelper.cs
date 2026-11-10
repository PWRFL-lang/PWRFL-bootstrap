using System;
using System.Collections.Generic;

namespace PWR.Compiler.Helpers;

internal static class LinqHelper
{
	public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : class
	{
		ArgumentNullException.ThrowIfNull(source, nameof(source));
		return WhereNotNullImpl(source);
	}

	private static IEnumerable<T> WhereNotNullImpl<T>(IEnumerable<T?> source) where T : class
	{
		foreach (T? item in source) {
			if (item != null) {
				yield return item;
			}
		}
	}
}
