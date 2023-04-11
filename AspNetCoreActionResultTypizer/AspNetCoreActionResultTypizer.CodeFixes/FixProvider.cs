using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace AspNetCoreActionResultTypizer
{
    using static SyntaxFactory;
    
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AspNetCoreActionResultTypizerCodeFixProvider)), Shared]
    public class AspNetCoreActionResultTypizerCodeFixProvider : CodeFixProvider
    {
        private static readonly ImmutableArray<string> _FixableDiagnosticIds = ImmutableArray.Create(AspNetCoreActionResultTypizerAnalyzer.DiagnosticId);
        
        public sealed override ImmutableArray<string> FixableDiagnosticIds => _FixableDiagnosticIds;

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken))!;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var methodDeclaration = root
                .FindToken(diagnosticSpan.Start)
                .Parent!
                .AncestorsAndSelf()
                .OfType<MethodDeclarationSyntax>()
                .First();

            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
            var analysisInfo = AspNetCoreActionResultTypizerAnalyzer.GetAnalysisInfo(
                semanticModel!.Compilation, semanticModel, methodDeclaration, context.CancellationToken); 

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.CodeFixTitle,
                    createChangedDocument: c => AspNetCoreActionResultTypizerAsync(context.Document, methodDeclaration, analysisInfo, c),
                    equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
                diagnostic);
        }

        private static async Task<Document> AspNetCoreActionResultTypizerAsync(
            Document document,
            MethodDeclarationSyntax methodDeclaration,
            AspNetCoreActionResultTypizerAnalyzer.AnalysisInfo analysisInfo,
            CancellationToken cancellationToken)
        {
            static IEnumerable<INamespaceSymbol> GetAllNamespacesOfTypes(ITypeSymbol type)
            {
                if (type.ContainingNamespace is not null)
                    yield return type.ContainingNamespace;
                if (type is not INamedTypeSymbol namedTypeSymbol)
                    yield break;
                foreach (var genericTypeParameter in namedTypeSymbol.TypeArguments)
                {
                    foreach (var namespaceSymbol in GetAllNamespacesOfTypes(genericTypeParameter))
                    {
                        yield return namespaceSymbol;
                    }
                }
            }
            var namespacesThatShouldBeImported = GetAllNamespacesOfTypes(analysisInfo.ArgumentType);
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken);
            
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var compilation = semanticModel!.Compilation;

            var actionResultType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ActionResult`1")!;
            var resolvedActionResultType = actionResultType.Construct(analysisInfo.ArgumentType);

            var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1")!; 
            ITypeSymbol newReturnType;
            if (analysisInfo.IsReturnTypeTask)
                newReturnType = taskType.Construct(resolvedActionResultType);
            else
                newReturnType = resolvedActionResultType;  
            
            // Make sure Task is imported if we're dealing with an async func
            if (analysisInfo.IsReturnTypeTask)
            {
                var taskNamespace = taskType.ContainingNamespace;
                namespacesThatShouldBeImported = namespacesThatShouldBeImported.Append(taskNamespace);
            }
            
            var format = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);
            var displayName = newReturnType.ToDisplayString(format);
            var newReturnTypeSyntax = ParseTypeName(displayName);
            
            {
                var previousTriviaBefore = methodDeclaration.ReturnType.GetLeadingTrivia();
                var previousTriviaAfter = methodDeclaration.ReturnType.GetTrailingTrivia();
                newReturnTypeSyntax = newReturnTypeSyntax
                    .WithLeadingTrivia(previousTriviaBefore)
                    .WithTrailingTrivia(previousTriviaAfter); 
            } 
            
            var newMethodDeclaration = methodDeclaration.WithReturnType(newReturnTypeSyntax);
            editor.ReplaceNode(methodDeclaration, newMethodDeclaration);

            // TODO: Add usings.
            // var compilationUnit = methodDeclaration.Ancestors().OfType<CompilationUnitSyntax>().First();
            // generator.AddNamespaceImports(compilationUnit, namespacesThatShouldBeImported.Select(UsingDirective()));
            
            var newDocument = editor.GetChangedDocument();
            return newDocument;
        }
    }
}
