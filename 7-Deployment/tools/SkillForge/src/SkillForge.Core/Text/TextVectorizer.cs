using System.Text.RegularExpressions;

namespace SkillForge.Core.Text;

/// <summary>
/// A small, dependency-free lexical vectorizer. Tokenizes to lowercase word stems,
/// drops stop words, and computes cosine similarity over term-frequency vectors.
/// Deterministic and offline by design — used for description-overlap detection and
/// the default routing strategy so the toolkit needs no API key in CI.
/// </summary>
public static class TextVectorizer
{
    private static readonly Regex Tokenizer = new(@"[a-z0-9]+", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the","a","an","and","or","but","to","of","in","on","for","with","is","are","be",
        "this","that","it","as","at","by","from","your","you","when","use","used","using",
        "do","not","if","into","about","which","what","how","can","will","should","its"
    };

    public static Dictionary<string, double> Vectorize(string text)
    {
        var counts = new Dictionary<string, double>();
        foreach (Match m in Tokenizer.Matches(text.ToLowerInvariant()))
        {
            var token = Normalize(m.Value);
            if (token.Length < 2 || StopWords.Contains(token)) continue;
            counts[token] = counts.TryGetValue(token, out var c) ? c + 1 : 1;
        }
        return counts;
    }

    /// <summary>Light normalization: strip a trailing plural 's' on longer tokens so
    /// 'errors' and 'error' match. Deliberately conservative — not a full stemmer.</summary>
    private static string Normalize(string token)
    {
        if (token.Length > 3 && token.EndsWith('s') && !token.EndsWith("ss") && !token.EndsWith("us"))
            return token[..^1];
        return token;
    }

    public static double CosineSimilarity(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        double dot = 0;
        foreach (var (k, v) in a)
            if (b.TryGetValue(k, out var bv)) dot += v * bv;

        double magA = Math.Sqrt(a.Values.Sum(v => v * v));
        double magB = Math.Sqrt(b.Values.Sum(v => v * v));
        return magA == 0 || magB == 0 ? 0 : dot / (magA * magB);
    }

    public static double Similarity(string left, string right) =>
        CosineSimilarity(Vectorize(left), Vectorize(right));
}
