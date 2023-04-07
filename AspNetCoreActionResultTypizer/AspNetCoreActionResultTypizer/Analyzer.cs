using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AspNetCoreActionResultTypizer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AspNetCoreActionResultTypizerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AspNetCoreActionResultTypizer";

    // https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md
    private static LocalizableString GetString(string name) => new LocalizableResourceString(name, Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString _Title = GetString(nameof(Resources.AnalyzerTitle));
    private static readonly LocalizableString _MessageFormat = GetString(nameof(Resources.AnalyzerMessageFormat));
    private static readonly LocalizableString _Description = GetString(nameof(Resources.AnalyzerDescription));
    private const string _Category = "Usage";

    private static readonly DiagnosticDescriptor _Rule = new DiagnosticDescriptor(DiagnosticId, _Title, _MessageFormat, _Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: _Description);
    private static readonly ImmutableArray<DiagnosticDescriptor> _SupportedDiagnostics = ImmutableArray.Create(_Rule);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => _SupportedDiagnostics;

    public override void Initialize(AnalysisContext context)
    {
        // https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.LocalDeclarationStatement);
    }

    private void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var localDeclaration = (LocalDeclarationStatementSyntax)context.Node;

        // make sure the declaration isn't already const:
        if (localDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return;
        }

        TypeSyntax variableTypeName = localDeclaration.Declaration.Type;
        ITypeSymbol variableType = context.SemanticModel.GetTypeInfo(variableTypeName, context.CancellationToken).ConvertedType;

        // Ensure that all variables in the local declaration have initializers that
        // are assigned with constant values.
        foreach (VariableDeclaratorSyntax variable in localDeclaration.Declaration.Variables)
        {
            EqualsValueClauseSyntax initializer = variable.Initializer;
            if (initializer == null)
            {
                return;
            }

            Optional<object> constantValue = context.SemanticModel.GetConstantValue(initializer.Value, context.CancellationToken);
            if (!constantValue.HasValue)
            {
                return;
            }

            // Ensure that the initializer value can be converted to the type of the
            // local declaration without a user-defined conversion.
            Conversion conversion = context.SemanticModel.ClassifyConversion(initializer.Value, variableType);
            if (!conversion.Exists || conversion.IsUserDefined)
            {
                return;
            }

            // Special cases:
            //  * If the constant value is a string, the type of the local declaration
            //    must be System.String.
            //  * If the constant value is null, the type of the local declaration must
            //    be a reference type.
            if (constantValue.Value is string)
            {
                if (variableType.SpecialType != SpecialType.System_String)
                {
                    return;
                }
            }
            else if (variableType.IsReferenceType && constantValue.Value != null)
            {
                return;
            }
        }

        // Perform data flow analysis on the local declaration.
        DataFlowAnalysis dataFlowAnalysis = context.SemanticModel.AnalyzeDataFlow(localDeclaration);

        foreach (VariableDeclaratorSyntax variable in localDeclaration.Declaration.Variables)
        {
            // Retrieve the local symbol for each variable in the local declaration
            // and ensure that it is not written outside of the data flow analysis region.
            ISymbol variableSymbol = context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken);
            if (dataFlowAnalysis.WrittenOutside.Contains(variableSymbol))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(_Rule, context.Node.GetLocation()));
    }
}
