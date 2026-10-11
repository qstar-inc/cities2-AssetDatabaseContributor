namespace AssetDatabaseContributor.Types
{
    public class LocaleRow
    {
        public int Prefix { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Lang { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
