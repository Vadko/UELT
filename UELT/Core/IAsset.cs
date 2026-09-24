namespace UELT.Core
{
    public interface IAsset
    {
        bool IsGood { get; }
        void SaveFile(string FilPath);
        List<List<string>> ExtractTexts();
        void ImportTexts(List<List<string>> strings);
        public List<string> GetAvailableLanguages() => new List<string>();
        public void SetSelectedLanguage(string language) { }
    }

    public static class AssetHelper
    {
        public static string ReplaceBreaklines(string StringValue, bool Back = false)
        {
            if (string.IsNullOrEmpty(StringValue))
                return StringValue;

            if (!Back)
            {
                return StringValue.Replace("\r\n", "<cf>").Replace("\r", "<cr>").Replace("\n", "<lf>").Replace("\t", "<tb>");
            }
            else
            {
                return StringValue.Replace("<cf>", "\r\n").Replace("<cr>", "\r").Replace("<lf>", "\n").Replace("<tb>", "\t");
            }
        }
    }
}