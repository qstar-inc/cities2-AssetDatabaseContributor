using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AssetDatabaseContributor.Systems
{
    public class AssetDataExtract
    {
        public string PrefabType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string GUID { get; set; } = string.Empty;
        public string SubPath { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string SourceVersion { get; set; } = string.Empty;

        public Dictionary<string, object> Components { get; set; } = new(0);
    }

    public record LocaleData
    {
        public int Prefix { get; set; } = 0;
        public string Name { get; set; } = string.Empty;
        public string Lang { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    public class ComponentAttributeRow
    {
        public string comp_name { get; set; }
        public int index { get; set; }
        public string attr_name { get; set; }
        public string attr_type { get; set; }
        public string note { get; set; }
    }

    public class LocaleRow
    {
        public int prefix { get; set; }
        public string name { get; set; }
        public string lang { get; set; }
        public string text { get; set; }
    }

    public class PrefabRow
    {
        public string guid { get; set; }
        public string name { get; set; }
        public string prefab_type { get; set; }

        //public string source { get; set; }
        //public string source_id { get; set; }
        //public string source_version { get; set; }
        public string sub_path { get; set; }
        public string path { get; set; }
    }

    public class ComponentRow
    {
        public string guid { get; set; }
        public string attr_id { get; set; }
        public string attr_value { get; set; }
    }

    public class SourceDataCache
    {
        [JsonProperty("server_time")]
        public long ServerTime { get; set; } = 0;

        [JsonProperty("source_data")]
        public List<SourceDataRow> Rows { get; set; }

        [JsonProperty("version")]
        public int Version { get; set; } = 0;
    }

    public class SourceDataRow : IEquatable<SourceDataRow>
    {
        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("source_id")]
        public string SourceId { get; set; }

        [JsonProperty("source_version")]
        public string SourceVersion { get; set; }

        public bool Equals(SourceDataRow other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return string.Equals(Source, other.Source, StringComparison.Ordinal)
                && string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
                && string.Equals(SourceVersion, other.SourceVersion, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals((SourceDataRow)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Source, SourceId, SourceVersion);
        }
    }

    public class ServerSourceDataResponse
    {
        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("server_time")]
        public long ServerTime { get; set; }

        [JsonProperty("source_data")]
        public List<SourceDataRow> SourceData { get; set; }
    }

    public class CollectedImage
    {
        public string Guid { get; set; }
        public string Extension { get; set; } // ".png", ".jpg", ".svg" etc
        public string Hash { get; set; } // SHA-256 hex string, lowercase
        public long Size { get; set; }
        public string FilePath { get; set; }
    }

    public class ImageManifestEntry
    {
        [JsonProperty("guid")]
        public string Guid { get; set; }

        [JsonProperty("extension")]
        public string Extension { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }
    }

    public class ImageManifestResponse
    {
        [JsonProperty("needed")]
        public List<string> Needed { get; set; }
    }

#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
}
