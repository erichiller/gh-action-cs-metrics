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
                                         _                        => throw new Exception()
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

    static string? toMemberSafeName(ISymbol member, string className) =>
        Extensions.TypeMermaidInfo.replaceAngleBracketsWithHtmlCodes(ToMemberName(member, className));

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
                index           = methodName.IndexOf($" {className}.", StringComparison.Ordinal) + 2 + className.Length;
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

public class TypeMermaidInfo {
    public string DiagramNodeId => toClassNameId(this.Name);

    public HashSet<string> Modifiers { get; } = new ();

    public HashSet<string> ImplementedTypes { get; } = new ();

    public required string Name { get; init; }

    public required string Namespace { get; set; }

    public HashSet<MemberMermaidInfo> Members { get; } = new ();


    public string ToMermaidClass() {
        StringBuilder builder = new ();

        foreach (var interfaceName in this.ImplementedTypes) {
            builder.AppendLine($"{toClassNameId(interfaceName)} <|-- {toClassNameId(this.Name)} : implements");
        }
        foreach (var member in this.Members) {
            if (member.ReturnType.ContainingNamespace?.ToDisplayString().StartsWith("System") != true) {
                builder.AppendLine($"{toClassNameId(member.ReturnType.ToDisplayName().TrimEnd('?'))} <-- {toClassNameId(this.Name)} : {member.Symbol.Name}");
            }
        }

        builder.AppendLine(
            $$"""
              class {{this.DiagramNodeId}} ["{{replaceAngleBracketsWithHtmlCodes(this.Name)}}"] {
              """);
        foreach (var modifier in this.Modifiers) {
            builder.AppendLine($"    <<{modifier}>>");
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


    static string toClassNameId(string className) =>
        className
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "_")
            .Replace("~", "_")
            .Replace(".", "_")
            .Replace("?", String.Empty);

    internal static string? replaceAngleBracketsWithHtmlCodes(string className) =>
        className
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}

public class CombinedMermaidDiagramInfo {
    public Dictionary<string, Dictionary<string, TypeMermaidInfo>> CombinedInfo { get; } = new ();

    public void Add(string assemblyDisplayName, string typeName) {
        // , IList<string> members 
        if (!CombinedInfo.ContainsKey(assemblyDisplayName)) {
            CombinedInfo[assemblyDisplayName] = new Dictionary<string, TypeMermaidInfo>();
        }
        if (!CombinedInfo[assemblyDisplayName].ContainsKey(typeName)) {
            CombinedInfo[assemblyDisplayName][typeName] =
                new TypeMermaidInfo {
                    Namespace = assemblyDisplayName,
                    Name      = typeName
                };
        }
    }

    // TODO: NOT SURE IF THE INTERFACE NAMESPACE IS KNOWN
    public void AddInterface(string assemblyDisplayName, string interfaceName, string? implementationTypeName = null) {
        this.Add(assemblyDisplayName, interfaceName);
        CombinedInfo[assemblyDisplayName][interfaceName].Modifiers.Add("interface");
        if (implementationTypeName is { } t) {
            this.Add(assemblyDisplayName, t);
            CombinedInfo[assemblyDisplayName][t].ImplementedTypes.Add(interfaceName);
        }
    }

    // TODO: NOT SURE IF THE INTERFACE NAMESPACE IS KNOWN
    public void AddBase(string assemblyDisplayName, ISymbol baseTypeSymbol, string? implementationTypeName = null) {
        string baseTypeName = baseTypeSymbol.ToDisplayName();
        this.Add(assemblyDisplayName, baseTypeName);
        if (baseTypeSymbol.IsAbstract) {
            CombinedInfo[assemblyDisplayName][baseTypeName].Modifiers.Add("interface");
        }
        if (implementationTypeName is { } t) {
            this.Add(assemblyDisplayName, t);
            CombinedInfo[assemblyDisplayName][t].ImplementedTypes.Add(baseTypeName);
        }
    }

    public void AddMember(string assemblyDisplayName, string typeName, MemberMermaidInfo memberInfo) {
        this.Add(assemblyDisplayName, typeName);
        CombinedInfo[assemblyDisplayName][typeName].Members.Add(memberInfo);
    }

    public string ToMermaidDiagram() {
        StringBuilder builder = new (CLASS_DIAGRAM_START_STRING);
        builder.AppendLine();

        foreach (var (namespaceName, types) in this.CombinedInfo) {
            // builder.AppendLine($"namespace {namespaceName} {{"); // TODO: GitHub's mermaid version doesn't support this ( as of 2023-10-22 )
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
    internal static string ToMermaidClassDiagram(this CodeAnalysisMetricData classMetric, string className, string namespaceSymbolName, CombinedMermaidDiagramInfo combinedDiagramInfo) {
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
        
        System.Console.WriteLine( "Name" + " = " +
            classMetric.Symbol.Name
        );
        System.Console.WriteLine( "ToDisplayString" + " = " +
            classMetric.Symbol.ToDisplayString( ) 
        );
        System.Console.WriteLine( nameof(SymbolDisplayFormat.MinimallyQualifiedFormat) + " = " +
            classMetric.Symbol.ToDisplayString( SymbolDisplayFormat.MinimallyQualifiedFormat ) 
        );

        className = className.Contains(".")
            ? className[(className.IndexOf(".", StringComparison.Ordinal) + 1)..]
            : className;

        TypeMermaidInfo singleType = new TypeMermaidInfo {
            Name      = className,
            Namespace = namespaceSymbolName
        };

        combinedDiagramInfo.Add(namespaceSymbolName, className);

        if (classMetric.Symbol is ITypeSymbol { BaseType: { Kind: SymbolKind.NamedType } baseType }) {
            singleType.ImplementedTypes.Add(baseType.ToDisplayString());
            combinedDiagramInfo.AddBase(namespaceSymbolName, baseType, className);
        }
        if (classMetric.Symbol is ITypeSymbol { Interfaces.Length: > 0 } typeSymbol) {
            foreach (var @interface in typeSymbol.Interfaces) {
                string interfaceName = @interface.Name;
                if (@interface.IsGenericType) {
                    var typeArgs = string.Join(",", @interface.TypeArguments.Select(ta => ta.Name));
                    interfaceName = $"{@interface.Name}<{typeArgs}>";
                }
                singleType.ImplementedTypes.Add(interfaceName);
                combinedDiagramInfo.AddInterface(namespaceSymbolName, interfaceName, className);
            }
        }

        var members = classMetric.Children.OrderBy(
            m => m.Symbol.Kind switch {
                     SymbolKind.Field    => 1,
                     SymbolKind.Property => 2,
                     SymbolKind.Method   => 3,
                     _                   => 4
                 }).ToArray();
        foreach (var member in members) {
            var memberMermaidInfo = new MemberMermaidInfo(member.Symbol);
            singleType.Members.Add(memberMermaidInfo);
            combinedDiagramInfo.AddMember(namespaceSymbolName, className, memberMermaidInfo);
        }


        var mermaidCode = CombinedMermaidDiagramInfo.CLASS_DIAGRAM_START_STRING + "\n" + singleType.ToMermaidClass();

        return mermaidCode;
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

    private static string getNamedTypeDisplayName(ISymbol symbol) {
        StringBuilder minimalTypeName =
            new (
                symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

        var containingType = symbol.ContainingType;
        while (containingType is not null) {
            minimalTypeName.Insert(0, ".");
            minimalTypeName.Insert(0, containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            containingType = containingType.ContainingType;
        }

        return minimalTypeName.ToString();
    }
}