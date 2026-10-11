using System.Collections.Generic;

namespace AssetDatabaseContributor.Types
{
    public class DataCacheComponent
    {
        public long ServerTime { get; set; }
        public Dictionary<
            string,
            Dictionary<string, List<ComponentAttributeRowCompact>>
        > ComponentData { get; set; } = new();
        public int Version { get; set; }
        public string Checksum { get; set; } = string.Empty;
    }
}
