using System.Collections.Generic;

namespace AssetDatabaseContributor.Types
{
    public class ServerComponentDataResponse
    {
        public int Count { get; set; }
        public long ServerTime { get; set; }
        public Dictionary<
            string,
            Dictionary<string, List<ComponentAttributeRowCompact>>
        > ComponentData { get; set; } = new();
    }
}
