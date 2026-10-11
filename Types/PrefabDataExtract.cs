using System.Collections.Generic;

namespace AssetDatabaseContributor.Types
{
    public class PrefabDataExtract
    {
        public string PrefabType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string GUID { get; set; } = string.Empty;
        public string SubPath { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string SourceVersion { get; set; } = string.Empty;
        public Dictionary<string, object> Components { get; set; } = new(0);
    }
}
