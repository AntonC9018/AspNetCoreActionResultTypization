using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.MethodDeclaration);
    }

    private void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax) context.Node;
        var returnTypeSyntax = methodDeclaration.ReturnType;
        var returnType = context.SemanticModel.GetTypeInfo(returnTypeSyntax, context.CancellationToken).ConvertedType as INamedTypeSymbol;
        if (returnType is null)
            return;
        var iActionResultSymbol = context.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.IActionResult");
        bool IsReturnTypeActionResult()
        {
            return SymbolEqualityComparer.Default.Equals(returnType, iActionResultSymbol);
        }
        bool IsReturnTypeTaskOfActionResult()
        {
            var taskSymbol = context.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
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
            return;

        if (isReturnTypeTask
            && !methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            throw new Exception("Method is not async but return type is Task<IActionResult>");
        }
        
        // check if method is in a controller
        var controllerBaseSymbol = context.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase");
        if (controllerBaseSymbol is null)
            throw new Exception("Expected to find Microsoft.AspNetCore.Mvc.ControllerBase");
                
        var classDeclaration = methodDeclaration.Parent as ClassDeclarationSyntax;
        if (classDeclaration == null)
            return;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (classSymbol is null)
            return;
        if (!classSymbol.AllInterfaces.Contains(controllerBaseSymbol))
            return;

        // Get the Ok method symbol of the controller class
        var okMethodSymbol = controllerBaseSymbol.GetMembers("Ok").OfType<IMethodSymbol>().First();
        InvocationExpressionSyntax? okInvocation = null;

        bool CheckIsInvocationOk(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression is NameSyntax nameSyntax
                // resolve the method in the current class context
                && context.SemanticModel.GetSymbolInfo(nameSyntax, context.CancellationToken).Symbol is
                    IMethodSymbol methodSymbol
                && SymbolEqualityComparer.Default.Equals(methodSymbol, okMethodSymbol);
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
            return;
        
        // Get the argument type passed into the Ok method
        var argument = okInvocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument is null)
            return;
        
        var argumentType = context.SemanticModel.GetTypeInfo(argument.Expression, context.CancellationToken).ConvertedType;
        if (argumentType is null)
            return;

        if (isReturnTypeTask)
        {
            
        }

        context.ReportDiagnostic(Diagnostic.Create(_Rule, context.Node.GetLocation()));
    }
}
