using System;
using System.Collections.Generic;
using System.Numerics;

namespace PWR.Compiler.Helpers;

internal static class Search
{
	/// <summary>
	/// Searches a sorted list for the first element whose selected key matches the target.
	/// </summary>
	/// <param name="arr">A sorted list, optionally containing duplicates, sorted by the selected key.</param>
	/// <param name="selector">A function to extract the comparison key from each element.</param>
	/// <param name="target">The key value to search for.</param>
	/// <returns>
	/// The index of the first element whose selected key matches <paramref name="target"/> if found;
	/// otherwise, the bitwise complement (~) of the index where it would be inserted
	/// to maintain sorted order.
	/// </returns>
	public static int BinarySearchFirst<T, U>(this List<T> arr, Func<T, U> selector, U target)
		where U : INumber<U>
	{
		var low = 0;
		var high = arr.Count - 1;
		var result = ~arr.Count;

		while (low <= high) {
			var mid = low + ((high - low) >> 1);

			U cmp = selector(arr[mid]) - target;

			if (cmp == U.Zero) {
				result = mid;
				high = mid - 1;
			} else if (cmp < U.Zero) {
				low = mid + 1;
			} else {
				result = ~low;
				high = mid - 1;
			}
		}

		return result;
	}
}
