/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Globalization;
using System.Linq;


namespace Database;

public static class TypeMapperExtensions
{
  public static string? Escape(this string? str) => str?.Replace(@"'", @"''");

  public static string FormatArray<T>(this T[] array) =>
    $"ARRAY[{string.Join(", ", array.Select(e => e.ToString()))}]::{typeof(T).Name}[]";
}

public static class Sql
{
  public static string Literal(string? value) =>
    value is null ? "NULL" : $"'{value.Escape()}'";

  public static string Literal(int? value) =>
    value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "NULL";

  public static string Literal(float? value) =>
    value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "NULL";

  public static string Literal(DateTime value) =>
    $"'{value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'";

  public static string Literal(Enum value) =>
    Literal(value.ToString());

  public static string Literal(bool value) =>
    value ? "TRUE" : "FALSE";
}
