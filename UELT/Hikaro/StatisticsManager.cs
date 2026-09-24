using System.Text.RegularExpressions;

namespace UELT.Hikaro;

public static class StatisticsManager
{
    private static readonly Regex WordRegex = new(@"[\p{L}\d]+(?:[_\-\*&'’–—-][\p{L}\d]+)*", RegexOptions.Compiled);
    private static readonly Regex PlaceholderRegex = new(@"\{[^}]+\}", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"^\d+$", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex PluralKeyRegex = new(@"\b(?:one|few|many|other|microsoft|sony|nintendo)=", RegexOptions.Compiled);
    private static readonly Regex PipePlatformRegex = new(@"\|(?:platform|gender|plural|text_gp|fmt)\(([^)]*)\)", RegexOptions.Compiled);

    public class StatsResult
    {
        public int TranslatedRows { get; set; }
        public int TranslatedWords { get; set; }
        public int TotalWords { get; set; }
        public int ApprovedRows { get; set; }
        public int ApprovedWords { get; set; }
    }

    public static async Task<StatsResult> CalculateStatsAsync(IEnumerable<DataGridItem> rows, CancellationToken ct = default)
    {
        if (rows == null)
            return new StatsResult();

        var snapshot = rows
            .Select(r => new { r.IsNew, r.Text, r.Translation, r.Status })
            .ToList();

        var result = await Task.Run(() =>
        {
            int translatedRows = 0, translatedWords = 0, totalWords = 0, approvedRows = 0, approvedWords = 0;

            foreach (var row in snapshot)
            {
                ct.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(row.Text))
                    totalWords += CountWords(row.Text);

                if (row.Translation != row.Text)
                {
                    translatedRows++;
                    if (!string.IsNullOrWhiteSpace(row.Text))
                        translatedWords += CountWords(row.Text);
                }

                if (row.Status == RowStatus.Approved && !string.IsNullOrWhiteSpace(row.Translation) && row.Translation != row.Text)
                {
                    approvedRows++;
                    if (!string.IsNullOrWhiteSpace(row.Text))
                        approvedWords += CountWords(row.Text);
                }
            }

            return new StatsResult
            {
                TranslatedRows = translatedRows,
                TranslatedWords = translatedWords,
                TotalWords = totalWords,
                ApprovedRows = approvedRows,
                ApprovedWords = approvedWords
            };
        }, ct);

        return result;
    }

    private static List<string> SplitByAsterisk(string word)
    {
        if (!word.Contains('*'))
            return new List<string> { word };

        var parts = word.Split('*', StringSplitOptions.RemoveEmptyEntries);
        return parts.ToList();
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        string cleaned = TagRegex.Replace(text, " ");
        cleaned = PlaceholderRegex.Replace(cleaned, " ");

        cleaned = PipePlatformRegex.Replace(cleaned, m =>
        {
            string inside = m.Groups[1].Value;
            inside = PluralKeyRegex.Replace(inside, "");
            var parts = inside.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        });

        var matches = WordRegex.Matches(cleaned);
        int count = 0;

        foreach (Match m in matches)
        {
            string word = m.Value;

            if (!NumberRegex.IsMatch(word))
            {
                if (word.Contains('_')) continue;

                var splitWords = SplitByAsterisk(word);
                foreach (var splitWord in splitWords)
                {
                    if (!string.IsNullOrEmpty(splitWord))
                        count++;
                }
            }
        }

        return count;
    }
}