﻿using System.ComponentModel;
using System.Text.RegularExpressions;

namespace DotNet.GitHubAction.Extensions;

public record MemberMermaidInfo(ISymbol Symbol) {
    public string     SignatureLine => Symbol.ToDisplayName();
    public SymbolKind Kind          => Symbol.Kind;

    public string ClassName => this.Symbol.ContainingType.ToDisplayName().Split('.')[^1];
    /*
     * this.Symbol switch {
       IFieldSymbol field         => field.ContainingType.ToDisplayName(),
       IPropertySymbol prop       => prop.ContainingType.ToDisplayName(),
       IMethodSymbol methodSymbol => methodSymbol.ContainingType.ToDisplayName(),
       _                          => "??????"
       };
     */

    public ITypeSymbol ReturnType => Symbol switch {
                                         IMethodSymbol method     => method.ReturnType,
                                         IPropertySymbol property => property.Type,
                                         IFieldSymbol field       => field.Type,
                                         _                        => throw new InvalidEnumArgumentException($"Unexpected symbol type {Symbol.GetType().Name} encountered on {Symbol.Name}")
                                     };

    public string? ToMermaidMemberLine() {
        string? memberLine = this.Symbol.Kind switch {
                                 SymbolKind.Field    => $"    {toMemberSafeName(this.Symbol, this.ClassName)}{ToClassifier(this.Symbol)}",
                                 SymbolKind.Property => $"    {toMemberSafeName(this.Symbol, this.ClassName)}{ToClassifier(this.Symbol)}",
                                 SymbolKind.Method   => $"    {toMemberSafeName(this.Symbol, this.ClassName)}",
                                 _                   => null
                             };
        return memberLine;
    }

    static string toMemberSafeName(ISymbol member, string className) =>
        MermaidUtils.ReplaceAngleBracketsWithHtmlCodes(ToMemberName(member, className));

    static string ToAccessModifier(ISymbol member) {
        // TODO: figure out how to get access modifiers.
        // + Public
        // - Private
        // # Protected
        // ~ Package / Internal

        return member switch {
                   IFieldSymbol field   => "-",
                   IPropertySymbol prop => "+",
                   IMethodSymbol method => "+",
                   _                    => "+"
               };
    }

    static string? ToClassifier(ISymbol member) =>
        (member.IsStatic, member.IsAbstract) switch {
            (true, _) => "$", (_, true) => "*", _ => null
        };


    static string ToMemberName(ISymbol member, string className) {
        var accessModifier = ToAccessModifier(member);
        if (member is IMethodSymbol { Kind: SymbolKind.Method } methodSymbol) {
            var methodName = member.ToDisplayName();
            if (methodSymbol.MethodKind == MethodKind.Constructor) {
                var ctor = methodName.Substring(methodName.IndexOf("(", StringComparison.Ordinal));
                return $"{accessModifier}.ctor{ctor} {className}";
            }

            var returnType = methodSymbol.ReturnType.ToString()!;
            if (returnType.Contains(".")) {
                goto regex;
            }

            var classNameOffset = className.Contains('.')
                ? className.Substring(className.IndexOf(".", StringComparison.Ordinal)).Length - 1
                : className.Length;

            var index           = returnType.Length + 2 + classNameOffset;
            var methodSignature = methodName.Substring(index);
            return $"{accessModifier}{methodSignature}{ToClassifier(member)} {returnType}";

            regex:
            Regex returnTypeRegex = new (@"^(?<returnType>\w+(?(<)[^\>]+>|[^ ]+))");
            if (returnTypeRegex.Match(methodName) is { Success: true } match) {
                // 2 is hardcoded for the space and "." characters
                index = methodName.IndexOf($" {className}.", StringComparison.Ordinal) + 2 + className.Length;
                if (index > methodName.Length) {
                    throw new Exception();
                }
                methodSignature = methodName.Substring(index);
                var memberClassifier = ToClassifier(member);
                returnType = match.Groups["returnType"].Value;
                var line = $"{accessModifier}{methodSignature}{memberClassifier} {returnType}";
                return line;
            }
        }

        return $"{accessModifier}{member.ToDisplayName().Replace($"{className}.", "")}";
    }
}

public class ImplementationInfo : IEquatable<ImplementationInfo?> {
    public string DiagramNodeId => _symbol.ToMermaidNodeId();

    public string Name => _symbol.Name;

    public string Namespace => _symbol.ContainingNamespace?.ToMermaidNodeId() ?? String.Empty;

    public string NameWithTypeParameters => this.Name + TypeParamsString;
    public string NameWithTypeArguments  => this.Name + TypeArgsString;

    public bool IsInterface => this._symbol switch {
                                   { TypeKind: TypeKind.Interface }                                                                      => true,
                                   { TypeKind: TypeKind.Error, Name: { Length: > 1 } name } when name[0] == 'I' && Char.IsUpper(name[1]) => true, // if it's an error symbol and it follows I[A-Z] then assume it is an interface from an external assembly.
                                   _                                                                                                     => false
                               };

    public string? TypeArgsString => _symbol.IsGenericType
        ? "<" + String.Join(",", _symbol.TypeArguments.Select(ta => ta.Name)) + ">"
        : null;
    public string? TypeParamsString => _symbol.IsGenericType
        ? "<" + String.Join(",", _symbol.TypeParameters.Select(tp => tp.ToDisplayName())) + ">"
        : null;

    private INamedTypeSymbol _symbol;

    public ImplementationInfo(INamedTypeSymbol symbol) {
        this._symbol = symbol;
        System.Console.WriteLine(
            $"""
             ImplementationInfo
             ==================
                 Name             = {symbol.Name}
                 DiagramNodeId    = {DiagramNodeId}
                 TypeArgsString   = {TypeArgsString}
                 TypeParamsString = {TypeParamsString}
             """);
    }

    public void ToMermaidDiagram(ref StringBuilder builder, bool withTypeArgs) {
        string displayName =
            withTypeArgs
                ? this.NameWithTypeArguments
                : this.NameWithTypeParameters;
        string s =
            $$"""
              class {{this.DiagramNodeId}} ["{{MermaidUtils.ReplaceAngleBracketsWithHtmlCodes(displayName)}}"] {
                  {{this._symbol.GetClassModifierString()}}
              }
              """;
        builder.AppendLine(s);
    }

    /*
     *
     */

    public override bool Equals(object? other) =>
        this.Equals(other as ImplementationInfo);

    public bool Equals(ImplementationInfo? other) =>
        other?.DiagramNodeId == this.DiagramNodeId;

    public override int GetHashCode() => this.DiagramNodeId.GetHashCode();
}

public class TypeMermaidInfo {
    public string DiagramNodeId => _symbol.ToMermaidNodeId();

    public HashSet<ImplementationInfo> ImplementedTypes { get; } = new ();

    public string Name => _symbol.Name;

    public string Namespace => _symbol.ContainingNamespace?.ToMermaidNodeId() ?? String.Empty;

    public HashSet<MemberMermaidInfo> Members { get; } = new ();

    private INamedTypeSymbol _symbol;

    public TypeMermaidInfo(ISymbol symbol) {
        // 
        if (symbol is not INamedTypeSymbol namedTypeSymbol) {
            throw new ArgumentException($"unexpected symbol type: {symbol.GetType().Name}");
        }
        this._symbol = namedTypeSymbol;

        if (symbol is ITypeSymbol { BaseType: { Kind: SymbolKind.NamedType } baseType }) {
            this.ImplementedTypes.Add(new (baseType));
        }

        if (symbol is ITypeSymbol { Interfaces.Length: > 0 } typeSymbol) {
            foreach (var implementedInterface in typeSymbol.Interfaces) {
                this.ImplementedTypes.Add(new ImplementationInfo(implementedInterface));
            }
        }

        var members = namedTypeSymbol.GetMembers()
                                     .Where(static symbol => symbol.Kind is SymbolKind.Field or SymbolKind.Property or SymbolKind.Method)
                                     .Where(static symbol => symbol.CanBeReferencedByName)
                                     .OrderBy(
                                         static m => m.Kind switch {
                                                         SymbolKind.Field    => 1,
                                                         SymbolKind.Property => 2,
                                                         SymbolKind.Method   => 3,
                                                         _                   => 4
                                                     }).ToArray();
        System.Console.WriteLine(
            "GetMembers() =" +
            String.Join(",", members.Select(m => m.Name))
        );
        foreach (var member in members) {
            var memberMermaidInfo = new MemberMermaidInfo(member);
            this.Members.Add(memberMermaidInfo);
        }
        //
        System.Console.WriteLine(
            $"""
             TypeMermaidInfo
             ==================
                 Name             = {_symbol.Name}
                 DiagramNodeId    = {DiagramNodeId}
             """);
        //
    }

    public string ToMermaidClass(bool withParentDefinitions = false) {
        StringBuilder builder = new ();

        foreach (var parent in this.ImplementedTypes) {
            builder.AppendLine($"{parent.DiagramNodeId} <|-- {this.DiagramNodeId} : " + (parent.IsInterface ? "implements" : "inherits"));
            if (withParentDefinitions) {
                // withTypeArgs because it is assumed that if withParentDefinitions then withTypeArgs is also desired
                parent.ToMermaidDiagram(ref builder, withTypeArgs: true);
            }
        }
        // don't draw relationships for enum types
        if (this._symbol.TypeKind != TypeKind.Enum) {
            foreach (var member in this.Members) {
                if (member.ReturnType.Kind == SymbolKind.ArrayType) {
                    // TODO: draw relationship to element type
                    continue;
                }
                if (member.ReturnType.Kind == SymbolKind.TypeParameter) {
                    continue;
                }

                // FUTURE TODO: Add links based on collections element types
                // FUTURE TODO: Add links based on generic type parameters type constraints?
                // string? ns = member.ReturnType.ContainingNamespace?.ToDisplayString();
                string? ns = member.ReturnType.ContainingNamespace?.Name;
                if (!String.IsNullOrWhiteSpace(ns) && ns?.StartsWith("System") != true) {
                    System.Console.WriteLine($"====> Drawing member return type relationship for {this.Name}.{member.Symbol.Name} to {member.ReturnType.Name}");
                    builder.AppendLine($"{member.ReturnType.ToMermaidNodeId().TrimEnd('?')} <-- {this.DiagramNodeId} : {member.Symbol.Name}");
                    if (withParentDefinitions) {
                        member.ReturnType.GetMermaidClassDeclaration(ref builder);
                    }
                }
            }
        }

        builder.AppendLine(
            $$"""
              class {{this.DiagramNodeId}} ["{{MermaidUtils.ReplaceAngleBracketsWithHtmlCodes(this._symbol.ToDisplayName())}}"] {
              """);
        if (this._symbol.GetClassModifierString() is { } modifierString) {
            builder.AppendLine($"    {modifierString}");
        }

        var members = this.Members.OrderBy(
            m => m.Symbol.Kind switch {
                     SymbolKind.Field    => 1,
                     SymbolKind.Property => 2,
                     SymbolKind.Method   => 3,
                     _                   => 4
                 }).ToArray();
        if (members.Length == 0) {
            builder.AppendLine("    "); // empty line for empty Type
        }
        foreach (var member in members) {
            builder.AppendLine(member.ToMermaidMemberLine());
        }
        builder.AppendLine("}");


        return builder.ToString();
    }
}

public class CombinedMermaidDiagramInfo {
    private Dictionary<string, Dictionary<string, TypeMermaidInfo>> CombinedInfo { get; } = new ();

    public TypeMermaidInfo Add(CodeAnalysisMetricData classMetric) {
        ISymbol symbol         = classMetric.Symbol;
        string  assemblyNodeId = symbol.ContainingNamespace?.ToMermaidNodeId() ?? String.Empty;
        string  typeNodeId     = symbol.ToMermaidNodeId();
        if (!CombinedInfo.ContainsKey(assemblyNodeId)) {
            CombinedInfo[assemblyNodeId] = new Dictionary<string, TypeMermaidInfo>();
        }
        if (!CombinedInfo[assemblyNodeId].ContainsKey(typeNodeId)) {
            CombinedInfo[assemblyNodeId][typeNodeId] =
                new TypeMermaidInfo(symbol);
        }
        return CombinedInfo[assemblyNodeId][typeNodeId];
    }

    public string ToMermaidDiagram() {
        StringBuilder builder = new (CLASS_DIAGRAM_START_STRING);
        builder.AppendLine();

        foreach (var (namespaceName, types) in this.CombinedInfo) {
            // builder.AppendLine($"namespace {namespaceName} {{"); // TODO: GitHub's mermaid version doesn't support this ( as of 2023-10-22 )
            System.Console.WriteLine($"drawing combined, namespace = '{namespaceName}'");
            // TODO: FILTER OPTION HERE
            foreach (var type in types.Values) {
                builder.AppendLine(type.ToMermaidClass());
            }
            // builder.AppendLine("}");
        }

        return builder.ToString();
    }

    internal const string CLASS_DIAGRAM_START_STRING = """
                                                       %%{init: {
                                                           'fontFamily': 'monospace'
                                                       } }%%
                                                       classDiagram
                                                       """;
}

static class CodeAnalysisMetricDataExtensions {
    internal static string ToCyclomaticComplexityEmoji(this CodeAnalysisMetricData metric) =>
        metric.CyclomaticComplexity switch {
            >= 0 and <= 7   => "✅",  // ✅ (was ✔️)
            8 or 9          => "⚠️", // ⚠️
            10 or 11        => "☢️", // ☢️
            >= 12 and <= 14 => "❌",  // ❌
            _               => "🤯"  // 🤯
        };

    internal static int CountNamespaces(this CodeAnalysisMetricData metric) =>
        metric.CountKind(SymbolKind.Namespace);

    internal static int CountNamedTypes(this CodeAnalysisMetricData metric) =>
        metric.CountKind(SymbolKind.NamedType);

    static int CountKind(this CodeAnalysisMetricData metric, SymbolKind kind) =>
        metric.Children
              .Flatten(child => child.Children)
              .Count(child => child.Symbol.Kind == kind);

    internal static (int Complexity, string Emoji) FindHighestCyclomaticComplexity(this CodeAnalysisMetricData metric) =>
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

    static IEnumerable<TSource> Flatten<TSource>(this IEnumerable<TSource> parent, Func<TSource, IEnumerable<TSource>> childSelector) =>
        parent.SelectMany(
                  source => childSelector(source).Flatten(childSelector))
              .Concat(parent);

    ///
    /// <param name="combinedDiagramInfo">
    ///     Namespace -> TypeName -> members
    /// </param>
    internal static string ToMermaidClassDiagram(this CodeAnalysisMetricData classMetric, CombinedMermaidDiagramInfo combinedDiagramInfo) {
        // https://mermaid-js.github.io/mermaid/#/classDiagram
        // https://github.com/mermaid-js/mermaid/blob/develop/packages/mermaid/src/schemas/config.schema.yaml#L168
        /* If title is desired, add like:
        ```mermaid
        ---
        title: Bank example
        ---
        %%{init: {
            'fontFamily': 'monospace'
        } }%%
        classDiagram
        */

        /*
        Name = Class1
        ToDisplayString = SomeRoot.SampleProject.Class1
        MinimallyQualifiedFormat = Class1
        FullyQualifiedFormat = global::SomeRoot.SampleProject.Class1
        */
        printNames(classMetric.Symbol);

        TypeMermaidInfo singleType = combinedDiagramInfo.Add(classMetric);


        var mermaidCode = CombinedMermaidDiagramInfo.CLASS_DIAGRAM_START_STRING
                          + "\n"
                          + singleType.ToMermaidClass(true);

        return mermaidCode;
    }

    private static void printNames(ISymbol symbol) {
        System.Console.WriteLine("Name" + " = " +
                                 symbol.Name
        );
        System.Console.WriteLine("ToMermaidNodeId" + " = " +
                                 symbol.ToMermaidNodeId()
        );
        System.Console.WriteLine("ToDisplayString" + " = " +
                                 symbol.ToDisplayString()
        );
        System.Console.WriteLine(nameof(SymbolDisplayFormat.MinimallyQualifiedFormat) + " = " +
                                 symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        );
        System.Console.WriteLine(nameof(SymbolDisplayFormat.FullyQualifiedFormat) + " = " +
                                 symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        );
        System.Console.WriteLine("ToDisplayName" + " = " +
                                 symbol.ToDisplayName()
        );
    }
}

internal static class SymbolExtensions {
    internal static string ToDisplayName(this CodeAnalysisMetricData metric) =>
        metric.Symbol.ToDisplayName();

    internal static string ToDisplayName(this ISymbol symbol) =>
        symbol.Kind switch {
            SymbolKind.Assembly  => symbol.Name,
            SymbolKind.NamedType => getNamedTypeDisplayName(symbol),
            SymbolKind.Method
                or SymbolKind.Field
                or SymbolKind.Event
                or SymbolKind.Property => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),

            _ => symbol.ToDisplayString()
        };

    private static SymbolDisplayFormat _fqDisplayFormat = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                                                                                  //genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
                                                                                  genericsOptions: SymbolDisplayGenericsOptions.None
    );

    internal static string ToMermaidNodeId(this ISymbol symbol) =>
        ToClassNameId(
            symbol switch {
                IAssemblySymbol    => symbol.Name,
                INamespaceSymbol   => symbol.Name,
                INamedTypeSymbol s => s.ToDisplayString(_fqDisplayFormat) + s.GetTypeParametersString(),
                _                  => throw new ArgumentException($"Invalid type of Symbol: {symbol.GetType().Name}")
            });

    public static string? GetTypeParametersString(this INamedTypeSymbol symbol) =>
        symbol.IsGenericType
            ? "<" + String.Join(",", symbol.TypeParameters.Select(tp => tp.ToDisplayName())) + ">"
            : null;


    public static void GetMermaidClassDeclaration(this ITypeSymbol symbol, ref StringBuilder builder) {
        //
        string s =
            $$"""
              class {{symbol.ToMermaidNodeId()}} ["{{MermaidUtils.ReplaceAngleBracketsWithHtmlCodes(symbol.ToDisplayName())}}"] {
                  {{symbol.GetClassModifierString()}}
              }
              """;
        builder.AppendLine(s);
    }

    public static string? GetClassModifierString(this ITypeSymbol symbol) {
        return symbol switch {
                   { TypeKind  : TypeKind.Interface }                                                                      => "<<interface>>",
                   { TypeKind  : TypeKind.Error, Name: { Length: > 1 } name } when name[0] == 'I' && Char.IsUpper(name[1]) => "<<interface>>", // if it's an error symbol and it follows I[A-Z] then assume it is an interface from an external assembly.
                   { TypeKind  : TypeKind.Enum }                                                                           => "<<Enum>>",
                   { TypeKind  : TypeKind.Struct, IsRecord: true, IsReadOnly: true, }                                      => "<<readonly record struct>>",
                   { TypeKind  : TypeKind.Struct, IsRecord: true }                                                         => "<<record struct>>",
                   { TypeKind  : TypeKind.Struct }                                                                         => "<<struct>>",
                   { IsRecord  : true }                                                                                    => "<<record>>",
                   { IsAbstract: true }                                                                                    => "<<abstract>>",
                   _                                                                                                       => null
               };
    }

    public static string ToClassNameId(string className) =>
        className
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "_")
            .Replace("~", "_")
            .Replace(".", "_")
            .Replace("?", String.Empty);

    private static string getNamedTypeDisplayName(ISymbol symbol) {
        StringBuilder minimalTypeName = new (symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

        var containingType = symbol.ContainingType;
        while (containingType is not null) {
            minimalTypeName.Insert(0, ".");
            minimalTypeName.Insert(0, containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            containingType = containingType.ContainingType;
        }

        return minimalTypeName.ToString();
    }
}

internal static class MermaidUtils {
    public static string ReplaceAngleBracketsWithHtmlCodes(string className) =>
        className
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}

/*

System.Console.WriteLine( "\n" +
                                      $"member ContainingNamespace ToDisplayString = {member.ReturnType.ContainingNamespace?.ToDisplayString()}\n\t" +
                                      $"member ContainingNamespace ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) = {member.ReturnType.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}\n\t" +
                                      $"member ContainingNamespace ToDisplayName = {member.ReturnType.ContainingNamespace?.ToDisplayName()}\n\t" +
                                      $"member ContainingNamespace Name = {member.ReturnType.ContainingNamespace?.Name}\n\t" +
                                      $"member ReturnType ToDisplayString = {member.ReturnType.ToDisplayString()}\n\t" +
                                      $"member ReturnType Kind = {member.ReturnType.Kind}\n\t" +
                                      $"member ReturnType GetType() = {member.ReturnType.GetType()}" );

            */