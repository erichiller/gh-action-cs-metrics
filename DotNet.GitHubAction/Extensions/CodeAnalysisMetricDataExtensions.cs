﻿using System.Text.RegularExpressions;

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

public class ImplementationInfo {
    public string DiagramNodeId => _symbol.ToMermaidNodeId();
    
    public string Name => _symbol.Name;

    public string Namespace => _symbol.ContainingNamespace?.ToMermaidNodeId() ?? String.Empty;
    
    private ISymbol _symbol;
    
    public ImplementationInfo (
        ISymbol symbol
    ) {
        this._symbol = symbol;
    }
}
    

public class TypeMermaidInfo {
    public string DiagramNodeId => _symbol.ToMermaidNodeId();

    public HashSet<string> Modifiers { get; } = new ();

    public HashSet<string> ImplementedTypes { get; } = new ();

    public string Name => _symbol.Name;

    public string Namespace => _symbol.ContainingNamespace?.ToMermaidNodeId() ?? String.Empty;

    public HashSet<MemberMermaidInfo> Members { get; } = new ();

    private INamedTypeSymbol _symbol;
    
    public TypeMermaidInfo ( CodeAnalysisMetricData classMetric ){
        var symbol = classMetric.Symbol;
        if( symbol is not INamedTypeSymbol namedTypeSymbol ){
            throw new ArgumentException($"unexpected symbol type: {symbol.GetType().Name}");
        }
        
        if (symbol is ITypeSymbol { BaseType: { Kind: SymbolKind.NamedType } baseType }) {
            this.ImplementedTypes.Add(baseType.ToDisplayString());
        }
        
        if ( symbol is ITypeSymbol { Interfaces.Length: > 0 } typeSymbol) {
            foreach (var implementedInterface in typeSymbol.Interfaces) {
                string interfaceName = implementedInterface.Name;
                System.Console.WriteLine( "INTERFACE:" );
                // printNames(implementedInterface);
                if (implementedInterface.IsGenericType) {
                    var typeArgs = String.Join(",", implementedInterface.TypeArguments.Select(ta => ta.Name));
                    interfaceName = $"{implementedInterface.Name}<{typeArgs}>";
                }
                this.ImplementedTypes.Add(interfaceName);
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
            this.Members.Add(memberMermaidInfo);
            
        }
        
        this._symbol = namedTypeSymbol;
    }

    public string ToMermaidClass() {
        StringBuilder builder = new ();

        foreach (var interfaceName in this.ImplementedTypes) {
            builder.AppendLine($"{SymbolExtensions.ToClassNameId(interfaceName)} <|-- {this.DiagramNodeId} : implements");
        }
        foreach (var member in this.Members) {
            System.Console.WriteLine( "\n" +
                                      $"member ContainingNamespace ToDisplayString = {member.ReturnType.ContainingNamespace?.ToDisplayString()}\n\t" +
                                      $"member ContainingNamespace ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) = {member.ReturnType.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}\n\t" +
                                      $"member ContainingNamespace ToDisplayName = {member.ReturnType.ContainingNamespace?.ToDisplayName()}\n\t" +
                                      $"member ContainingNamespace Name = {member.ReturnType.ContainingNamespace?.Name}\n\t" +
                                      $"member ReturnType ToDisplayString = {member.ReturnType.ToDisplayString()}\n\t" +
                                      $"member ReturnType Kind = {member.ReturnType.Kind}\n\t" +
                                      $"member ReturnType GetType() = {member.ReturnType.GetType()}" );
            
            if( member.ReturnType.Kind == SymbolKind.ArrayType ){
                // TODO: draw relationship to element type
                continue;
            }
            if( member.ReturnType.Kind == SymbolKind.TypeParameter ){
                continue;
            }
            
            // FUTURE TODO: Add links based on collections element types
            // FUTURE TODO: Add links based on generic type parameters type constraints?
            // string? ns = member.ReturnType.ContainingNamespace?.ToDisplayString();
            string? ns = member.ReturnType.ContainingNamespace?.Name;
            if ( !String.IsNullOrWhiteSpace(ns) && ns?.StartsWith("System") != true ) {
                System.Console.WriteLine( $"====> Drawing relationship." );
                builder.AppendLine($"{member.ReturnType.ToClassNameId().TrimEnd('?'))l} <-- {this.DiagramNodeId} : {member.Symbol.Name}");
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

    internal static string? replaceAngleBracketsWithHtmlCodes(string className) =>
        className
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}

public class CombinedMermaidDiagramInfo {
    private Dictionary<string, Dictionary<string, TypeMermaidInfo>> CombinedInfo { get; } = new ();

    public TypeMermaidInfo Add( CodeAnalysisMetricData classMetric ) { 
        ISymbol symbol = classMetric.Symbol;
        string assemblyNodeId = symbol.ContainingNamespace?.ToMermaidNodeId() ?? String.Empty;
        string typeNodeId = symbol.ToMermaidNodeId();
        if (!CombinedInfo.ContainsKey(assemblyNodeId)) {
            CombinedInfo[assemblyNodeId] = new Dictionary<string, TypeMermaidInfo>();
        }
        if (!CombinedInfo[assemblyNodeId].ContainsKey(typeNodeId)) {
            CombinedInfo[assemblyNodeId][typeNodeId] =
                new TypeMermaidInfo( classMetric );
        }
        return CombinedInfo[assemblyNodeId][typeNodeId];
    }

    // TODO: NOT SURE IF THE INTERFACE NAMESPACE IS KNOWN
    /*
    public void AddInterface( ISymbol symbol ) {
        var typeInfo = this.Add( symbol );
        CombinedInfo[typeInfo.Namespace][typeInfo.Name].Modifiers.Add("interface");
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
    */

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
        // System.Console.WriteLine( nameof(SymbolDisplayFormat.GlobalNamespaceStyle) + " = " +
        //     classMetric.Symbol.ToDisplayString( SymbolDisplayFormat.GlobalNamespaceStyle ) 
        // );
        
        /*
        className = className.Contains(".")
            ? className[(className.IndexOf(".", StringComparison.Ordinal) + 1)..]
            : className;
        */

        TypeMermaidInfo singleType = combinedDiagramInfo.Add( classMetric );

        


        var mermaidCode = CombinedMermaidDiagramInfo.CLASS_DIAGRAM_START_STRING + "\n" + singleType.ToMermaidClass();

        return mermaidCode;
    }
    
    private static void printNames( ISymbol symbol ){
        
        System.Console.WriteLine( "Name" + " = " +
            symbol.Name
        );
        System.Console.WriteLine( "ToMermaidNodeId" + " = " +
                                  symbol.ToMermaidNodeId( ) 
        );
        System.Console.WriteLine( "ToDisplayString" + " = " +
                                  symbol.ToDisplayString( ) 
        );
        System.Console.WriteLine( nameof(SymbolDisplayFormat.MinimallyQualifiedFormat) + " = " +
            symbol.ToDisplayString( SymbolDisplayFormat.MinimallyQualifiedFormat ) 
        );
        System.Console.WriteLine( nameof(SymbolDisplayFormat.FullyQualifiedFormat) + " = " +
            symbol.ToDisplayString( SymbolDisplayFormat.FullyQualifiedFormat ) 
        );
        System.Console.WriteLine( "ToDisplayName" + " = " +
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
                                                                          genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    internal static string ToMermaidNodeId(this ISymbol symbol) =>
        ToClassNameId(
        symbol switch {
            IAssemblySymbol  => symbol.Name,
            INamespaceSymbol => symbol.Name,
            INamedTypeSymbol => symbol.ToDisplayString(_fqDisplayFormat),
            _                    => throw new ArgumentException($"Invalid type of Symbol: {symbol.GetType().Name}")
        } );
        
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