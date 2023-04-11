using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using System.Threading;
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
    public static DiagnosticDescriptor Rule => _Rule;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => _SupportedDiagnostics;
    public override void Initialize(AnalysisContext context)
    {
        // https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.MethodDeclaration);
    }

    public class AnalysisInfo
    {
        public bool IsReturnTypeTask;
        public ITypeSymbol ArgumentType;
        public ITypeSymbol MethodReturnType;

        public AnalysisInfo(bool isReturnTypeTask, ITypeSymbol argumentType, ITypeSymbol methodReturnType)
        {
            IsReturnTypeTask = isReturnTypeTask;
            ArgumentType = argumentType;
            MethodReturnType = methodReturnType;
        }
    }

    public static AnalysisInfo? GetAnalysisInfo(
        Compilation compilation,
        SemanticModel semanticModel,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        var returnTypeSyntax = methodDeclaration.ReturnType;
        var returnType = semanticModel.GetTypeInfo(returnTypeSyntax, cancellationToken).ConvertedType as INamedTypeSymbol;
        if (returnType is null)
            return null;
        var iActionResultSymbol = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.IActionResult");
        bool IsReturnTypeActionResult()
        {
            return SymbolEqualityComparer.Default.Equals(returnType, iActionResultSymbol);
        }
        bool IsReturnTypeTaskOfActionResult()
        {
            var taskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            if (taskSymbol is null)
                throw new Exception("Expected to find System.Threading.Tasks.Task");
            if (!SymbolEqualityComparer.Default.Equals(returnType, taskSymbol))
                return false;
            var taskTypeArgument = returnType!.TypeArguments.FirstOrDefault();
            return SymbolEqualityComparer.Default.Equals(taskTypeArgument, iActionResultSymbol);
        }

        bool isReturnTypeTask;
        if (IsReturnTypeActionResult())
            isReturnTypeTask = false;
        else if (IsReturnTypeTaskOfActionResult())
            isReturnTypeTask = true;
        else
            return null;

        if (isReturnTypeTask
            && !methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            return null;
        }
        
        // check if method is in a controller
        var controllerSymbol = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase");
        if (controllerSymbol is null)
            throw new Exception("Expected to find Microsoft.AspNetCore.Mvc.ControllerBase");
                
        var classDeclaration = methodDeclaration.Parent as ClassDeclarationSyntax;
        if (classDeclaration == null)
            return null;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
        if (classSymbol is null)
            return null;
        
        
        
        bool IsDerivedFromType(ITypeSymbol type, ITypeSymbol expectedBase)
        {
            do
            {
                if (SymbolEqualityComparer.Default.Equals(type, expectedBase))
                    return true;
            }
            while ((type = type.BaseType!) is not null);

            return false;
        }
        if (!IsDerivedFromType(classSymbol, controllerSymbol))
            return null;

        // Get the Ok method symbol of the controller class
        var okMethodSymbol = controllerSymbol
            .GetMembers("Ok")
            .OfType<IMethodSymbol>()
            .First(s => s.Parameters.Length == 1);
        InvocationExpressionSyntax? okInvocation = null;

        bool CheckIsInvocationOk(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression is NameSyntax nameSyntax
                // resolve the method in the current class context
                && semanticModel.GetSymbolInfo(nameSyntax, cancellationToken).Symbol is
                    IMethodSymbol methodSymbol
                && SymbolEqualityComparer.Default.Equals(methodSymbol.OriginalDefinition, okMethodSymbol);
        }

        {
            var block = methodDeclaration.Body;
            if (block is not null)
            {
                okInvocation = block
                    .DescendantNodes()
                    .OfType<ReturnStatementSyntax>()
                    .Select(x => x.Expression)
                    .OfType<InvocationExpressionSyntax>()
                    .Where(CheckIsInvocationOk)
                    .FirstOrDefault();
            }
        }
        {
            var arrowExpression = methodDeclaration.ExpressionBody;
            if (arrowExpression is { Expression: InvocationExpressionSyntax invocation }
                && CheckIsInvocationOk(invocation))
            {
                okInvocation = invocation; 
            }
        }
        if (okInvocation is null)
            return null;
        
        // Get the argument type passed into the Ok method
        var argument = okInvocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument is null)
            return null;
        
        var argumentType = semanticModel.GetTypeInfo(argument.Expression, cancellationToken).Type;
        if (argumentType is null)
            return null;

        if (argumentType.SpecialType is SpecialType.System_Object
            || argumentType.IsAnonymousType)
        {
            return null;
        }
        
        return new AnalysisInfo(isReturnTypeTask, argumentType, returnType);
    }
    
    private void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax) context.Node;
        var analysisInfo = GetAnalysisInfo(context.Compilation, context.SemanticModel,methodDeclaration, context.CancellationToken);

        if (analysisInfo is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(_Rule, methodDeclaration.ReturnType.GetLocation()));
    }
}
