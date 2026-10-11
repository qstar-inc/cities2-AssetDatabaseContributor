namespace AssetDatabaseContributor.Types
{
    public class CollectedImage
    {
        public string GUID { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public long Size { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }
}
