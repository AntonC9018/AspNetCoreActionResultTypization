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

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // Find the type declaration identified by the diagnostic.
            var declaration = root.FindToken(diagnosticSpan.Start)!.Parent!.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().First();

            // Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.CodeFixTitle,
                    createChangedDocument: c => AspNetCoreActionResultTypizerAsync(context.Document, declaration, c),
                    equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
                diagnostic);
        }

        private static async Task<Document> AspNetCoreActionResultTypizerAsync(Document document,
            LocalDeclarationStatementSyntax localDeclaration,
            CancellationToken cancellationToken)
        {
            // Remove the leading trivia from the local declaration.
            var firstToken = localDeclaration.GetFirstToken();
            var leadingTrivia = firstToken.LeadingTrivia;
            var trimmedLocal = localDeclaration.ReplaceToken(
                firstToken, firstToken.WithLeadingTrivia(SyntaxTriviaList.Empty));

            // Create a const token with the leading trivia.
            var constToken = Token(
                leadingTrivia, 
                SyntaxKind.ConstKeyword, 
                TriviaList(ElasticMarker));

            // Insert the const token into the modifiers list, creating a new modifiers list.
            var newModifiers = trimmedLocal.Modifiers.Insert(0, constToken);

            // If the type of the declaration is 'var', create a new type name
            // for the inferred type.
            var variableDeclaration = localDeclaration.Declaration;
            var variableTypeName = variableDeclaration.Type;
            if (variableTypeName.IsVar)
            {
                var semanticModel = (await document.GetSemanticModelAsync(cancellationToken))!;

                // Special case: Ensure that 'var' isn't actually an alias to another type
                // (e.g. using var = System.String).
                var aliasInfo = semanticModel.GetAliasInfo(variableTypeName, cancellationToken);
                if (aliasInfo == null)
                {
                    // Retrieve the type inferred for var.
                    var type = semanticModel.GetTypeInfo(variableTypeName, cancellationToken).ConvertedType!;

                    // Special case: Ensure that 'var' isn't actually a type named 'var'.
                    if (type.Name != "var")
                    {
                        // Create a new TypeSyntax for the inferred type. Be careful
                        // to keep any leading and trailing trivia from the var keyword.
                        var typeName = ParseTypeName(type.ToDisplayString())
                            .WithLeadingTrivia(variableTypeName.GetLeadingTrivia())
                            .WithTrailingTrivia(variableTypeName.GetTrailingTrivia());

                        // Add an annotation to simplify the type name.
                        var simplifiedTypeName = typeName.WithAdditionalAnnotations(Simplifier.Annotation);

                        // Replace the type in the variable declaration.
                        variableDeclaration = variableDeclaration.WithType(simplifiedTypeName);
                    }
                }
            }
            // Produce the new local declaration.
            var newLocal = trimmedLocal
                .WithModifiers(newModifiers)
                .WithDeclaration(variableDeclaration);

            // Add an annotation to format the new local declaration.
            var formattedLocal = newLocal.WithAdditionalAnnotations(Formatter.Annotation);

            // Replace the old local declaration with the new local declaration.
            var oldRoot = (await document.GetSyntaxRootAsync(cancellationToken))!;
            var newRoot = oldRoot.ReplaceNode(localDeclaration, formattedLocal);

            // Return document with transformed tree.
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
