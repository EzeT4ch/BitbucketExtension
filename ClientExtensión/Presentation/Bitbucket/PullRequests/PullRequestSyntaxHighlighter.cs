using System.Runtime.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ClientExtension.Presentation.Bitbucket.PullRequests;

internal static class PullRequestSyntaxHighlighter
{
    private static readonly HashSet<string> ContextualKeywords = new(StringComparer.Ordinal)
    {
        "add", "alias", "and", "ascending", "async", "await", "by", "descending", "dynamic",
        "equals", "file", "from", "get", "global", "group", "init", "into", "join", "let",
        "managed", "nameof", "nint", "not", "notnull", "nuint", "on", "or", "orderby",
        "partial", "record", "remove", "required", "scoped", "select", "set", "unmanaged",
        "value", "var", "when", "where", "with", "yield",
    };

    internal static IReadOnlyList<PullRequestDiffToken> Highlight(string text, string? filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return [new PullRequestDiffToken(text, "Plain")];
        }

        var tokens = new List<PullRequestDiffToken>();
        /*foreach (var syntaxToken in SyntaxFactory.ParseTokens(text))
        {
            AppendTrivia(tokens, syntaxToken.LeadingTrivia);
            if (!syntaxToken.IsKind(SyntaxKind.EndOfFileToken) && syntaxToken.Text.Length > 0)
            {
                Append(tokens, syntaxToken.Text, ClassifyToken(syntaxToken));
            }

            AppendTrivia(tokens, syntaxToken.TrailingTrivia);
        }*/

        return tokens.Count == 0 ? [new PullRequestDiffToken(text, "Plain")] : tokens;
    }

    private static void AppendTrivia(List<PullRequestDiffToken> tokens, SyntaxTriviaList triviaList)
    {
        foreach (var trivia in triviaList)
        {
            var kind = trivia.Kind();
            var classification = kind switch
            {
                SyntaxKind.SingleLineCommentTrivia or
                SyntaxKind.MultiLineCommentTrivia or
                SyntaxKind.SingleLineDocumentationCommentTrivia or
                SyntaxKind.MultiLineDocumentationCommentTrivia => "Comment",
                _ when kind.ToString().EndsWith("DirectiveTrivia", StringComparison.Ordinal) => "Preprocessor",
                _ => "Plain",
            };
            Append(tokens, trivia.ToFullString(), classification);
        }
    }

    private static string ClassifyToken(SyntaxToken token)
    {
        var kind = token.Kind();
        if (SyntaxFacts.IsKeywordKind(kind) || ContextualKeywords.Contains(token.ValueText))
        {
            return "Keyword";
        }

        if (kind == SyntaxKind.NumericLiteralToken)
        {
            return "Number";
        }

        var kindName = kind.ToString();
        if (kind is SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken or
            SyntaxKind.Utf8StringLiteralToken or SyntaxKind.InterpolatedStringTextToken ||
            kindName.Contains("Interpolated", StringComparison.Ordinal) &&
            kindName.Contains("String", StringComparison.Ordinal))
        {
            return "String";
        }

        if (kind == SyntaxKind.IdentifierToken &&
            token.ValueText.Length > 0 &&
            char.IsUpper(token.ValueText[0]))
        {
            return "Type";
        }

        return "Plain";
    }

    private static void Append(List<PullRequestDiffToken> tokens, string text, string classification)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (tokens.Count > 0 && tokens[^1].Classification == classification)
        {
            tokens[^1] = new PullRequestDiffToken(tokens[^1].Text + text, classification);
            return;
        }

        tokens.Add(new PullRequestDiffToken(text, classification));
    }
}

[DataContract]
internal sealed record PullRequestDiffToken(
    [property: DataMember] string Text,
    [property: DataMember] string Classification);
