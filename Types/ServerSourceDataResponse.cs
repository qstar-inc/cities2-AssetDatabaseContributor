using System.Collections.Generic;

namespace AssetDatabaseContributor.Types
{
    public class ServerSourceDataResponse
    {
        public int Count { get; set; }
        public int CodeVersion { get; set; }
        public long ServerTime { get; set; }
        public List<List<object>> SourceData { get; set; } = new();
        public string GameVersion { get; set; } = string.Empty;
    }
}
