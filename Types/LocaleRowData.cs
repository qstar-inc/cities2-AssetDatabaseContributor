namespace AssetDatabaseContributor.Types
{
    public record LocaleRowData
    {
        public int Prefix { get; set; } = 0;
        public string Name { get; set; } = string.Empty;
        public string Lang { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
