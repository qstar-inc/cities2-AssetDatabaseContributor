using System;

namespace AssetDatabaseContributor.Types
{
    public class SourceDataRow : IEquatable<SourceDataRow>
    {
        public string SourceId { get; set; } = string.Empty;
        public string SourceVersion { get; set; } = string.Empty;

        public bool Equals(SourceDataRow other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
                && string.Equals(SourceVersion, other.SourceVersion, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals((SourceDataRow)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SourceId, SourceVersion);
        }
    }
}
