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
        private static readonly ImmutableArray<string> _FixableDiagnosticIds =
            ImmutableArray.Create(AspNetCoreActionResultTypizerAnalyzer.DiagnosticId);

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

            var action = CodeAction.Create(
                title: CodeFixResources.CodeFixTitle,
                createChangedDocument: c =>
                {
                    return AspNetCoreActionResultTypizerAsync(context.Document, methodDeclaration, analysisInfo, c);
                },
                equivalenceKey: nameof(CodeFixResources.CodeFixTitle));
            
            context.RegisterCodeFix(action, diagnostic);
        }

        private static async Task<Document> AspNetCoreActionResultTypizerAsync(
            Document document,
            MethodDeclarationSyntax methodDeclaration,
            AspNetCoreActionResultTypizerAnalyzer.AnalysisInfo analysisInfo,
            CancellationToken cancellationToken)
        {
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

            var generator = editor.Generator;
            var typeSyntax = (TypeSyntax)generator.TypeExpression(newReturnType);
            typeSyntax = typeSyntax.WithAdditionalAnnotations(Simplifier.AddImportsAnnotation);
            typeSyntax = typeSyntax.WithTriviaFrom(methodDeclaration.ReturnType);

            var newMethodDeclaration = methodDeclaration.WithReturnType(typeSyntax);
            editor.ReplaceNode(methodDeclaration, newMethodDeclaration);

            var newDocument = editor.GetChangedDocument();
            return newDocument;
        }
    }
}