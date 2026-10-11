using Game.Assets;

namespace AssetDatabaseContributor.Types
{
    public class MapDataExtract
    {
        public string Name { get; set; } = string.Empty;
        public string GUID { get; set; } = string.Empty;
        public string GUIDMeta { get; set; } = string.Empty;
        public string SubPath { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string SourceVersion { get; set; } = string.Empty;
        public MapInfo Metadata { get; set; } = new();
    }
}
