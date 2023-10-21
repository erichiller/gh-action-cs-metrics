﻿using System.Reflection.Metadata;
using System.Text.RegularExpressions;

namespace DotNet.GitHubAction.Extensions;

static class CodeAnalysisMetricDataExtensions
{
    internal static string ToCyclomaticComplexityEmoji(this CodeAnalysisMetricData metric) =>
        metric.CyclomaticComplexity switch
        {
            >= 0 and <= 7 => "✅",                 // ✅ (was ✔️)
            8 or 9 => "⚠️",                        // ⚠️
            10 or 11 => "☢️",                      // ☢️
            >= 12 and <= 14 => "❌",               // ❌
            _ => "🤯"                              // 🤯
        };

    internal static int CountNamespaces(this CodeAnalysisMetricData metric) =>
        metric.CountKind(SymbolKind.Namespace);

    internal static int CountNamedTypes(this CodeAnalysisMetricData metric) =>
        metric.CountKind(SymbolKind.NamedType);

    static int CountKind(this CodeAnalysisMetricData metric, SymbolKind kind) =>
        metric.Children
            .Flatten(child => child.Children)
            .Count(child => child.Symbol.Kind == kind);

    internal static (int Complexity, string Emoji) FindHighestCyclomaticComplexity(
        this CodeAnalysisMetricData metric) =>
        metric.Children
            .Flatten(child => child.Children)
            .Where(child =>
                child.Symbol.Kind is not SymbolKind.Assembly
                and not SymbolKind.Namespace
                and not SymbolKind.NamedType)
            .Select(m => (Metric: m, m.CyclomaticComplexity))
            .OrderByDescending(_ => _.CyclomaticComplexity)
            .Select(_ => (_.CyclomaticComplexity, _.Metric.ToCyclomaticComplexityEmoji()))
            .FirstOrDefault();

    static IEnumerable<TSource> Flatten<TSource>(
        this IEnumerable<TSource> parent, Func<TSource, IEnumerable<TSource>> childSelector) =>
        parent.SelectMany(
            source => childSelector(source).Flatten(childSelector))
            .Concat(parent);

    internal static string ToMermaidClassDiagram(
        this CodeAnalysisMetricData classMetric, string className)
    {
        // https://mermaid-js.github.io/mermaid/#/classDiagram
        StringBuilder builder = new("classDiagram");
        builder.AppendLine();

        className = className.Contains(".")
                    ? className.Substring(className.IndexOf(".") + 1)
                    : className;
        /*
        string classNameId = className
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace(",", "_")
                .Replace(" ", "_");
        string classNameDisplay = className
                .Replace("<", "&lt;")
                .Replace(">","&gt;");
        
        string classNameEscaped = 
            "`"
            + className
                .Replace("<", "&lt;")
                .Replace(">","&gt;")
            + "`";
        */

        if (classMetric.Symbol is ITypeSymbol typeSymbol &&
            typeSymbol.Interfaces.Length > 0)
        {
            foreach (var @interface in typeSymbol.Interfaces)
            {
                string interfaceName = @interface.Name;
                if (@interface.IsGenericType) {
                    var typeArgs = string.Join(",", @interface.TypeArguments.Select(ta => ta.Name));
                    interfaceName = $"{@interface.Name}<{typeArgs}>";
                }
                builder.AppendLine($"{toClassNameId(interfaceName)} <|-- {toClassNameId(className)} : implements"); 
                
                builder.AppendLine($$"""
                    class {{toClassNameId(interfaceName)}} ["{{classNameToDisplay(interfaceName)}}"] {
                        <<interface>>
                    }
                    """ );
            }
        }

        builder.AppendLine(
            $$"""
            class {{toClassNameId(className)}} ["{{classNameToDisplay(className)}}"] {
            """ );
            
        
        
        static string? classNameToDisplay( string className ) =>
             className
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        
        
        static string toClassNameId( string className ) =>
            className
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace(",", "_")
                .Replace(" ", "_")
                .Replace("~", "_");
        
        /*
        static string escapeClassName( string className ) =>
             '`'
             + className
                  .Replace("<", "&lt;")
                  .Replace(">", "&gt;")
             + '`';
        */
        
        static string? ToClassifier(CodeAnalysisMetricData member) =>
            (member.Symbol.IsStatic, member.Symbol.IsAbstract) switch
            {
                (true, _) => "$", (_, true) => "*", _ => null
            };

        static string ToAccessModifier(CodeAnalysisMetricData member)
        {
            // TODO: figure out how to get access modifiers.
            // + Public
            // - Private
            // # Protected
            // ~ Package / Internal

            return member.Symbol switch
            {
                IFieldSymbol field => "-",
                IPropertySymbol prop => "+",
                IMethodSymbol method => "+",
                _ => "+"
            };
        }
        
        static string toMemberSafeName(CodeAnalysisMetricData member, string className) =>
            classNameToDisplay(ToMemberName( member, className));

        static string ToMemberName(CodeAnalysisMetricData member, string className)
        {
            var accessModifier = ToAccessModifier(member);
            if (member.Symbol.Kind is SymbolKind.Method)
            {
                var method = member.ToDisplayName();
                var ctorMethod = $"{className}.{className}";
                if (method.StartsWith(ctorMethod))
                {
                    var ctor = method.Substring(ctorMethod.Length);
                    return $"{accessModifier}.ctor{ctor} {className}";
                }

                if (member.Symbol is IMethodSymbol methodSymbol)
                {
                    var rtrnType = methodSymbol.ReturnType.ToString()!;
                    if (rtrnType.Contains("."))
                    {
                        goto regex;
                    }

                    var classNameOffset = className.Contains(".")
                        ? className.Substring(className.IndexOf(".")).Length - 1
                        : className.Length;

                    var index = rtrnType.Length + 2 + classNameOffset;
                    var methodSignature = method.Substring(index);
                    return $"{accessModifier}{methodSignature}{ToClassifier(member)} {rtrnType}";
                }

            regex:
                Regex returnType = new(@"^(?<returnType>[\S]+)");
                if (returnType.Match(method) is { Success: true } match)
                {
                    // 2 is hardcoded for the space and "." characters
                    var index = method.IndexOf(" ") + 2 + className.Length;
                    var methodSignature = method.Substring(index);
                    return $"{accessModifier}{methodSignature}{ToClassifier(member)} {match.Groups["returnType"]}";
                }
            }

            return $"{accessModifier}{member.ToDisplayName().Replace($"{className}.", "")}";
        }
        
        var members = classMetric.Children.OrderBy(
               m => m.Symbol.Kind switch
               {
                   SymbolKind.Field => 1,
                   SymbolKind.Property => 2,
                   SymbolKind.Method => 3,
                   _ => 4
               }).ToArray();
        if ( members.Length == 0 ){
            builder.AppendLine("    "); // empty line for empty Type
        }
        foreach (var member in members ) {
            _ = member.Symbol.Kind switch
            {
                SymbolKind.Field => builder.AppendLine(
                    $"    {toMemberSafeName(member, className)}{ToClassifier(member)}"),

                SymbolKind.Property => builder.AppendLine(
                    $"    {toMemberSafeName(member, className)}{ToClassifier(member)}"),

                SymbolKind.Method => builder.AppendLine(
                    $"    {toMemberSafeName(member, className)}"),

                _ => null
            };
        }

        builder.AppendLine("}");

        var mermaidCode = builder.ToString();

        return mermaidCode;
    }

    internal static string ToDisplayName(this CodeAnalysisMetricData metric) =>
        metric.Symbol.Kind switch
        {
            SymbolKind.Assembly => metric.Symbol.Name,

            SymbolKind.NamedType => DisplayName(metric.Symbol),

            SymbolKind.Method
            or SymbolKind.Field
            or SymbolKind.Event
            or SymbolKind.Property => metric.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),

            _ => metric.Symbol.ToDisplayString()
        };

    static string DisplayName(ISymbol symbol)
    {
        StringBuilder minimalTypeName =
            new(
                symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

        var containingType = symbol.ContainingType;
        while (containingType is not null)
        {
            minimalTypeName.Insert(0, ".");
            minimalTypeName.Insert(0,
                containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            containingType = containingType.ContainingType;
        }

        return minimalTypeName.ToString();
    }
}
