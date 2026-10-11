namespace AssetDatabaseContributor.Types
{
    public class MapDataRow
    {
        public string GUID { get; set; } = string.Empty;
        public string MetadataGUID { get; set; } = string.Empty;
        public string ThumbnailGUID { get; set; } = string.Empty;
        public string PreviewGUID { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SubPath { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Theme { get; set; } = string.Empty;
        public float TempMin { get; set; }
        public float TempMax { get; set; }
        public float Cloudiness { get; set; }
        public float Precipitation { get; set; }
        public float Latitude { get; set; }
        public float Longitude { get; set; }
        public float BuildableLand { get; set; }
        public float Area { get; set; }
        public float SurfaceWaterAvailability { get; set; }
        public float GroundWaterAvailability { get; set; }
        public float ResFertile { get; set; }
        public float ResForest { get; set; }
        public float ResOil { get; set; }
        public float ResOre { get; set; }
        public float ResFish { get; set; }
        public int ConnRoad { get; set; }
        public int ConnTrain { get; set; }
        public int ConnAir { get; set; }
        public int ConnShip { get; set; }
        public int ConnElectricity { get; set; }
        public int ConnWater { get; set; }
        public string ContentPrerequisites { get; set; } = string.Empty;
        public int NameAsCityName { get; set; }
        public int StartingYear { get; set; }
    }
}
