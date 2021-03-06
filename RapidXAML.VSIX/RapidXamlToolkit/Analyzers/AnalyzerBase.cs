﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using RapidXamlToolkit.Logging;
using RapidXamlToolkit.Options;
using IServiceProvider = System.IServiceProvider;

namespace RapidXamlToolkit.Analyzers
{
    public class AnalyzerBase
    {
        public const string UnknownOrInvalidTypeName = "*UNKNOWN-INVALID-TYPE*";  // Asterisk as first character ensures it is invalid

        public const string NoPropertiesXaml = "<!-- No accessible properties when copying as XAML -->";

        public const string GridWithRowDefsIndicator = "GRID-PLUS-ROWDEFS";

        public const string GridWithRowDefs2ColsIndicator = "GRID-PLUS-ROWDEFS-2COLS";

        public AnalyzerBase(ILogger logger)
        {
            Logger = logger;
        }

        public static IServiceProvider ServiceProvider { get; set; }

        public static ILogger Logger { get; set; }

        public virtual string FileExtension { get; } = string.Empty;

        protected static string[] TypesToSkipWhenCheckingForSubProperties { get; } = new[] { "String", "ValueType", "Object" };

        protected static string[] NamesOfPropertiesToExcludeFromOutput { get; } = new[] { "IsInDesignMode", "IsInDesignModeStatic", "DataStore" };

        public static Settings GetSettings()
        {
            var configuredSettings = new ConfiguredSettings(ServiceProvider);

            return configuredSettings.ActualSettings;
        }

        public static (string output, int counter) GetPropertyOutputAndCounterForActiveProfile(PropertyDetails property, int numericSubstitute, Func<(List<string> strings, int count)> getSubPropertyOutput = null)
        {
            var settings = GetSettings();
            var activeProfile = settings.GetActiveProfile();
            return GetPropertyOutputAndCounter(activeProfile, property, numericSubstitute, getSubPropertyOutput);
        }

        public static string GetClassGroupingForActiveProfile()
        {
            var settings = GetSettings();
            var profile = settings.GetActiveProfile();
            var result = profile.ClassGrouping.Trim();

            Logger?.RecordInfo($"Using class grouping of {result}");

            return result;
        }

        public static string GetPropertyOutput(Profile profile, string type, string name, bool isReadOnly, Func<(List<string> strings, int count)> getSubProperties = null)
        {
            return GetPropertyOutputAndCounter(profile, new PropertyDetails { PropertyType = type, Name = name, IsReadOnly = isReadOnly }, 1, getSubProperties).output;
        }

        public static (string output, int counter) GetSubPropertyOutputAndCounter(Profile profile, string name, int numericSubstitute)
        {
            // Type is blank as it's can't be used in a subproperty
            return FormatOutput(profile, profile.SubPropertyOutput, type: string.Empty, name: name, numericSubstitute: numericSubstitute, symbol: null, getSubPropertyOutput: null);
        }

        public static (string output, int counter) GetPropertyOutputAndCounter(Profile profile, PropertyDetails property, int numericSubstitute, Func<(List<string> strings, int count)> getSubPropertyOutput = null)
        {
            var mappingOfInterest = GetMappingOfInterest(profile, property);

            string rawOutput = null;

            if (mappingOfInterest != null)
            {
                rawOutput = mappingOfInterest.Output;
            }
            else
            {
                if (property.PropertyType.IsGenericTypeName())
                {
                    var wildcardGenericType = property.PropertyType.Substring(0, property.PropertyType.ToCSharpFormat().IndexOf("<", StringComparison.Ordinal)) + "<T>";

                    Logger?.RecordInfo($"Searching for mapping for generic type treated as {wildcardGenericType}");

                    mappingOfInterest = GetMappingOfInterest(profile, wildcardGenericType, property.Name, property.IsReadOnly);

                    if (mappingOfInterest != null)
                    {
                        rawOutput = mappingOfInterest.Output;
                    }
                }

                if (rawOutput == null)
                {
                    Logger?.RecordInfo("No mapping found so using fallback output.");
                    rawOutput = profile?.FallbackOutput;
                }
            }

            if (rawOutput == null)
            {
                return (null, numericSubstitute);
            }

            return FormatOutput(profile, rawOutput, property.PropertyType, property.Name, numericSubstitute, property.Symbol, getSubPropertyOutput);
        }

        public static (string output, int counter) FormatOutput(Profile profile, string rawOutput, string type, string name, int numericSubstitute, ITypeSymbol symbol, Func<(List<string> strings, int count)> getSubPropertyOutput)
        {
            Logger?.RecordInfo($"Formatting output for property '{name}'");

            var result = rawOutput.Replace(Placeholder.PropertyName, name)
                                  .Replace(Placeholder.PropertyNameWithSpaces, name.AddSpacesToCamelCase());

            if (type.IsGenericTypeName())
            {
                Logger?.RecordInfo($"Formatting output for generic type '{type}'");

                type = type.ToCSharpFormat();
                var typeArgument = type.Substring(type.IndexOf("<", StringComparison.Ordinal) + 1, type.Length - type.IndexOf("<", StringComparison.Ordinal) - 2);
                result = result.Replace(Placeholder.PropertyType, typeArgument.AsXamlFriendlyTypeArgument());
            }
            else
            {
                Logger?.RecordInfo($"Formatting output for non-generic type '{type}'");

                result = result.Replace(Placeholder.PropertyType, type.AsXamlFriendlyTypeArgument());
            }

            if (rawOutput.Contains(Placeholder.SubProperties))
            {
                bool subPropertyInsideGridPlusRowDefs =
                    (rawOutput.IndexOf(Placeholder.SubProperties, StringComparison.OrdinalIgnoreCase)
                    > rawOutput.IndexOf(GridWithRowDefsIndicator, StringComparison.OrdinalIgnoreCase)
                 && rawOutput.IndexOf("/" + GridWithRowDefsIndicator, StringComparison.OrdinalIgnoreCase)
                    > rawOutput.IndexOf(Placeholder.SubProperties, StringComparison.OrdinalIgnoreCase))
                    ||
                    (rawOutput.IndexOf(Placeholder.SubProperties, StringComparison.OrdinalIgnoreCase)
                    > rawOutput.IndexOf(GridWithRowDefs2ColsIndicator, StringComparison.OrdinalIgnoreCase)
                    && rawOutput.IndexOf("/" + GridWithRowDefs2ColsIndicator, StringComparison.OrdinalIgnoreCase)
                    > rawOutput.IndexOf(Placeholder.SubProperties, StringComparison.OrdinalIgnoreCase));

                var subProps = getSubPropertyOutput?.Invoke();

                var replacement = new StringBuilder();

                if (!result.StartsWith(Placeholder.SubProperties))
                {
                    // put the sub-property on a new line if there's other output first
                    replacement.AppendLine();
                }

                if (subProps.HasValue)
                {
                    Logger?.RecordInfo($"Found {subProps.Value.strings.Count} subproperties");

                    foreach (var subProp in subProps.Value.strings)
                    {
                        replacement.AppendLine(subProp);
                    }
                }

                result = result.Replace(Placeholder.SubProperties, replacement.ToString());

                if (subPropertyInsideGridPlusRowDefs)
                {
                    Logger?.RecordInfo("Formatting subproperties inside a grid.");
                    var opener = new StringBuilder();

                    opener.AppendLine("<Grid>");

                    if (rawOutput.Contains(GridWithRowDefs2ColsIndicator))
                    {
                        Logger?.RecordInfo("Adding ColumnDefinitions to grid.");

                        opener.AppendLine("<Grid.ColumnDefinitions>");
                        opener.AppendLine("<ColumnDefinition Width=\"Auto\" />");
                        opener.AppendLine("<ColumnDefinition Width=\"*\" />");
                        opener.AppendLine("</Grid.ColumnDefinitions>");
                    }

                    if (subProps.HasValue)
                    {
                        opener.AppendLine("<Grid.RowDefinitions>");

                        Logger?.RecordInfo($"Adding {subProps.Value.count} row definitions.");

                        for (int i = 1; i <= subProps.Value.count; i++)
                        {
                            opener.AppendLine(i < subProps.Value.count
                                ? "<RowDefinition Height=\"Auto\" />"
                                : "<RowDefinition Height=\"*\" />");
                        }

                        opener.Append("</Grid.RowDefinitions>");
                    }

                    var placeHolderPos = result.IndexOf(GridWithRowDefs2ColsIndicator, StringComparison.InvariantCultureIgnoreCase);
                    if (placeHolderPos > -1)
                    {
                        var placeHolderEndPos = result.IndexOf(">", placeHolderPos, StringComparison.InvariantCultureIgnoreCase);

                        result = result.Substring(0, placeHolderPos - 1) + opener + result.Substring(placeHolderEndPos + 1);

                        placeHolderPos = result.IndexOf("</" + GridWithRowDefs2ColsIndicator + ">", StringComparison.InvariantCultureIgnoreCase);

                        result = result.Substring(0, placeHolderPos) + "</Grid>" + result.Substring(placeHolderPos + GridWithRowDefs2ColsIndicator.Length + 3);
                    }

                    placeHolderPos = result.IndexOf(GridWithRowDefsIndicator, StringComparison.InvariantCultureIgnoreCase);

                    if (placeHolderPos > -1)
                    {
                        var placeHolderEndPos = result.IndexOf(">", placeHolderPos, StringComparison.InvariantCultureIgnoreCase);

                        result = result.Substring(0, placeHolderPos - 1) + opener + result.Substring(placeHolderEndPos + 1);

                        placeHolderPos = result.IndexOf("</" + GridWithRowDefsIndicator + ">", StringComparison.InvariantCultureIgnoreCase);

                        result = result.Substring(0, placeHolderPos) + "</Grid>" + result.Substring(placeHolderPos + GridWithRowDefsIndicator.Length + 3);
                    }
                }
            }

            if (result.Contains(Placeholder.EnumMembers))
            {
                var enumMembers = symbol.GetMembers().Where(m => m.Kind == SymbolKind.Field && !m.IsImplicitlyDeclared).ToList();

                var replacement = new StringBuilder();

                if (!result.StartsWith(Placeholder.EnumMembers))
                {
                    // put the elements on a new line if there's other output first
                    replacement.AppendLine();
                }

                Logger?.RecordInfo($"Found {enumMembers.Count} members of enum");

                if (enumMembers.Any())
                {
                    foreach (var member in enumMembers)
                    {
                        var line = profile.EnumMemberOutput.Replace(Placeholder.EnumElement, member.Name)
                                                           .Replace(Placeholder.EnumElementWithSpaces, member.Name.AddSpacesToCamelCase())
                                                           .Replace(Placeholder.EnumPropName, name);

                        replacement.AppendLine(line);
                    }
                }

                result = result.Replace(Placeholder.EnumMembers, replacement.ToString());
            }

            while (result.Contains(Placeholder.IncrementingInteger))
            {
                Logger?.RecordInfo("Replacing incrementing integer placeholder.");

                var subPosition = result.IndexOf(Placeholder.IncrementingInteger, StringComparison.OrdinalIgnoreCase);

                result = result.Remove(subPosition, Placeholder.IncrementingInteger.Length);
                result = result.Insert(subPosition, numericSubstitute.ToString());

                numericSubstitute += 1;
            }

            while (result.Contains(Placeholder.RepeatingInteger))
            {
                Logger?.RecordInfo("Replacing repeated integer placeholder.");

                var subPosition = result.IndexOf(Placeholder.RepeatingInteger, StringComparison.OrdinalIgnoreCase);

                result = result.Remove(subPosition, Placeholder.RepeatingInteger.Length);
                result = result.Insert(subPosition, (numericSubstitute - 1).ToString()); // Remove 1 as was incremented after last used
            }

            return (result, numericSubstitute);
        }

        public static Mapping GetMappingOfInterest(Profile profile, PropertyDetails property)
        {
            // Enums can be mapped by name or that they're enums - check enum first
            if (property.Symbol?.BaseType?.Name == "Enum")
            {
                var enumMapping = GetMappingOfInterest(profile, "enum", property.Name, property.IsReadOnly);

                if (enumMapping != null)
                {
                    Logger?.RecordInfo($"Found mapping for '{property.Name}' as an Enum.");
                    return enumMapping;
                }
                else
                {
                    Logger?.RecordInfo($"No mapping found for '{property.Name}' as an Enum so now checking by type name.");
                }
            }

            return GetMappingOfInterest(profile, property.PropertyType, property.Name, property.IsReadOnly);
        }

        public static Mapping GetMappingOfInterest(Profile profile, string type, string name, bool isReadOnly)
        {
            if (profile == null)
            {
                return null;
            }

            var typeMappings = profile.Mappings.Where(m => type.ToCSharpFormat().MatchesAnyOfInCSharpFormat(m.Type)).ToList();

            if (!isReadOnly)
            {
                Logger?.RecordInfo("Property is not read-only so filtering out read-only mappings.");
                typeMappings = typeMappings.Where(m => m.IfReadOnly == false).ToList();
            }

            Mapping mappingOfInterest = null;

            // Readonly types match readonly mappings first
            if (isReadOnly)
            {
                Logger?.RecordInfo("Property is read-only so looking for read-only mappings first.");
                mappingOfInterest = typeMappings.FirstOrDefault(m => name.ToLowerInvariant().ContainsAnyOf(m.NameContains.ToLowerInvariant()) && m.IfReadOnly)
                                 ?? typeMappings.FirstOrDefault(m => string.IsNullOrWhiteSpace(m.NameContains) && m.IfReadOnly);
            }

            // writeable types don't match readonly mappings
            // readonly types match writeable mappings if no readonly mappings
            if (mappingOfInterest == null)
            {
                Logger?.RecordInfo("Looking for mappings that are not read-only.");
                mappingOfInterest = typeMappings.FirstOrDefault(m => name.ToLowerInvariant().ContainsAnyOf(m.NameContains.ToLowerInvariant()) && !m.IfReadOnly)
                                 ?? typeMappings.FirstOrDefault(m => string.IsNullOrWhiteSpace(m.NameContains) && !m.IfReadOnly);
            }

            return mappingOfInterest;
        }

        public static string GetSelectionPropertiesName(List<string> names)
        {
            if (names == null || !names.Any())
            {
                return string.Empty;
            }

            var result = names.First();

            if (names.Count == 2)
            {
                result += $" and {names[1]}";
            }
            else if (names.Count > 2)
            {
                var others = names.Count == 3 ? "other property" : "other properties";

                result += $", {names[1]} and {names.Count - 2} {others}";
            }

            return result;
        }

        public static string FormattedClassGroupingOpener(string classGrouping)
        {
            switch (classGrouping.ToUpperInvariant())
            {
                case GridWithRowDefsIndicator:
                case GridWithRowDefs2ColsIndicator:
                    return "Grid";
                default:
                    return classGrouping;
            }
        }

        public static string FormattedClassGroupingCloser(string classGrouping)
        {
            if (classGrouping.Contains(" "))
            {
                return classGrouping.Substring(0, classGrouping.IndexOf(" ", StringComparison.Ordinal));
            }

            switch (classGrouping.ToUpperInvariant())
            {
                case GridWithRowDefsIndicator:
                case GridWithRowDefs2ColsIndicator:
                    return "Grid";
                default:
                    return classGrouping;
            }
        }
    }
}
