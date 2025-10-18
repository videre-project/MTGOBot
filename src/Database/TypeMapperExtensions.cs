/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Linq;


namespace Database;

public static class TypeMapperExtensions
{
  public static string? Escape(this string? str) => str?.Replace(@"'", @"''");

  public static string FormatArray<T>(this T[] array) =>
    $"ARRAY[{string.Join(", ", array.Select(e => e.ToString()))}]::{typeof(T).Name}[]";
}
