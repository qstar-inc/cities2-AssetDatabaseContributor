using System.Collections.Generic;

namespace AssetDatabaseContributor.Types
{
    public class DataCacheSource
    {
        public long ServerTime { get; set; }
        public List<Dictionary<string, string[]>> Rows { get; set; } = new();
        public int Version { get; set; }
        public string Checksum { get; set; } = string.Empty;
    }
}
