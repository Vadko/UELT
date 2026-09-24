using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace UELT.Hikaro;

public class TagVariableHighlighter : DocumentColorizingTransformer
{
    private static readonly Regex Pattern =
        new(@"<[^<>\r\n]+>|\{[^{}\r\n]+\}", RegexOptions.Compiled);

    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        foreach (Match m in Pattern.Matches(text))
        {
            int start = line.Offset + m.Index;
            int end = start + m.Length;
            ChangeLinePart(start, end, el =>
            {
                el.TextRunProperties.SetForegroundBrush(
                    (Brush)Application.Current.Resources["TagVariableHighlightBrush"]);
                el.TextRunProperties.SetBackgroundBrush(
                    (Brush)Application.Current.Resources["TagVariableHighlightBackgroundBrush"]);
            });
        }
    }
}