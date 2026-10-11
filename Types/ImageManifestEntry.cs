namespace AssetDatabaseContributor.Types
{
    public class ImageManifestEntry
    {
        public string GUID { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
