using System.Collections.Generic;

namespace AssetDatabaseContributor.Types
{
    public class FileListResponse
    {
        public List<string> Needed { get; set; } = new();
    }
}
