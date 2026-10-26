using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace WeaveSharp.Projection.Spike;

public sealed class ProjectionSnapshot
{
    public ProjectionSnapshot(string document, string revision, SourceText text,
        CSharpParseOptions? options = null)
    {
        Document = document;
        Revision = revision;
        Text = text;
        Tree = CSharpSyntaxTree.ParseText(text,
            options ?? new CSharpParseOptions(LanguageVersion.CSharp14), path: document);
    }

    public ProjectionSnapshot(string document, string revision, string text,
        CSharpParseOptions? options = null) : this(document, revision, SourceText.From(text), options) { }

    public string Document { get; }
    public string Revision { get; }
    public SourceText Text { get; }
    public SyntaxTree Tree { get; }

    public IEnumerable<SyntaxNode> Bodies() => Tree.GetRoot().DescendantNodes()
        .Select(node => node switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression,
            LambdaExpressionSyntax lambda => lambda.Body,
            AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
            PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
            _ => null
        }).OfType<SyntaxNode>();

    public ProjectionGraph Project(SyntaxNode body)
    {
        if (!ReferenceEquals(body.SyntaxTree, Tree) || body is not (BlockSyntax or ExpressionSyntax))
            throw new ArgumentException("Choose a block or expression from this snapshot.", nameof(body));
        return new BodyProjector(this, body).Build();
    }
}
