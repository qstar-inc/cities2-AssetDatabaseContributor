using System.Collections.Generic;
using System.Globalization;
using Colossal.IO.AssetDatabase;
using CSVFile;
using Game;
using Game.Prefabs;
using Newtonsoft.Json;
using StarQ.Shared.Extensions;

namespace AssetDatabaseContributor.Systems
{
    public partial class ExtractionSystem : GameSystemBase
    {
        internal static HashSet<(
            int index,
            string comp,
            string attr,
            string type,
            string note,
            string source
        )> ComponentLib { get; set; } = new();

        internal static HashSet<(
            int index,
            string comp,
            string attr,
            string type,
            string note,
            string source
        )> BaseComponent { get; set; } = new();

        internal static readonly HashSet<string> ImagesNeeded = new();
        internal static HashSet<string> ModsToCheck = new();
        internal static HashSet<string> ModsToReject = new();
        internal static HashSet<string> ModsAdded = new();

        public static FileSystemDataSource.PathEscapePolicy kPathEscapePolicy = new();

        internal static JsonSerializerSettings GetJsonSetting()
        {
            JsonSerializerSettings settings = new()
            {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) =>
                {
                    LogHelper.SendLog(
                        $"Serialization error on property '{args.ErrorContext.Member}': {args.ErrorContext.Error.Message}"
                    );
                    args.ErrorContext.Handled = true;
                },
            };
            return settings;
        }

        internal static string SerializeInvariant<T>(IEnumerable<T> rows)
            where T : class, new()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                return CSV.Serialize(rows, GetCSVSetting());
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        internal static CSVSettings GetCSVSetting()
        {
            CSVSettings settings = new()
            {
                FieldDelimiter = '\t',
                TextQualifier = '~',
                LineSeparator = "\n",
                HeaderRowIncluded = true,
                ForceQualifiers = false,
            };
            return settings;
        }

        private void GetModLocalesToExtract(
            PrefabBase prefabBase,
            string sourceId,
            string sourceVersion,
            Dictionary<
                (string SourceId, string SourceVersion),
                List<(string Prefix, string Key)>
            > localesToExtract
        )
        {
            var key = (sourceId, sourceVersion);

            if (!localesToExtract.TryGetValue(key, out var list))
            {
                list = new List<(string Prefix, string Key)>();
                localesToExtract[key] = list;
            }

            if (prefabBase is UIAssetMenuPrefab || prefabBase is ServicePrefab)
            {
                list.Add(("Services.NAME", prefabBase.name));
                list.Add(("Services.DESCRIPTION", prefabBase.name));
            }
            else if (prefabBase is UIAssetCategoryPrefab)
            {
                list.Add(("SubServices.NAME", prefabBase.name));
                list.Add(("Assets.SUB_SERVICE_DESCRIPTION", prefabBase.name));
            }
            else if (prefabBase.Has<ServiceUpgrade>())
            {
                list.Add(("Assets.UPGRADE_NAME", prefabBase.name));
                list.Add(("Assets.UPGRADE_DESCRIPTION", prefabBase.name));
            }
            else
            {
                list.Add(("Assets.NAME", prefabBase.name));
                list.Add(("Assets.DESCRIPTION", prefabBase.name));
            }
        }

        private void GetMapLocalesToExtract(
            string identification,
            string sourceId,
            string sourceVersion,
            Dictionary<
                (string SourceId, string SourceVersion),
                List<(string Prefix, string Key)>
            > localesToExtract
        )
        {
            var key = (sourceId, sourceVersion);

            if (!localesToExtract.TryGetValue(key, out var list))
            {
                list = new List<(string Prefix, string Key)>();
                localesToExtract[key] = list;
            }

            if (identification != null)
            {
                list.Add(("Maps.MAP_TITLE", identification));
                list.Add(("Maps.MAP_DESCRIPTION", identification));
            }
        }

        private static int GetLocalePrefixId(string prefix)
        {
            return prefix switch
            {
                "Assets.NAME" => 1,
                "Assets.DESCRIPTION" => 2,
                "Assets.UPGRADE_NAME" => 3,
                "Assets.UPGRADE_DESCRIPTION" => 4,
                "Services.NAME" => 5,
                "Services.DESCRIPTION" => 6,
                "SubServices.NAME" => 7,
                "Assets.SUB_SERVICE_DESCRIPTION" => 8,
                "Maps.MAP_TITLE" => 9,
                "Maps.MAP_DESCRIPTION" => 10,
                _ => 0,
            };
        }

        //private void LoadExistingComponentLib()
        //{
        //    BaselineLib.Clear();
        //    ComponentLib.Clear();
        //    Assembly? assembly = ModHelper.GetModExecutable(Mod.Id)?.assembly;

        //    if (assembly == null)
        //    {
        //        LogHelper.SendLog("Something went wrong, could not find own assembly");
        //        return;
        //    }

        //    using Stream resource = assembly.GetManifestResourceStream(
        //        $"{Mod.Id}.EmbedCustom.components_attr.tsv.gz"
        //    );

        //    if (resource == null)
        //    {
        //        LogHelper.SendLog("Something went wrong, could not find own embedded TSV");
        //        return;
        //    }

        //    using GZipStream gzip = new(resource, CompressionMode.Decompress);
        //    using StreamReader streamReader = new(gzip);
        //    using CSVReader reader = new(streamReader, GetCSVSetting());

        //    List<ComponentAttributeRow> rows = reader.Deserialize<ComponentAttributeRow>();
        //    HashSet<(int, string, string, string, string, string)> baseline = new();

        //    foreach (ComponentAttributeRow row in rows)
        //        baseline.Add(
        //            (row.Index, row.CompName, row.AttrName, row.AttrType, row.Note, row.Source)
        //        );

        //    BaselineLib = baseline;
        //}
    }
}
