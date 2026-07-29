using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Common;
using CSVFile;
using Game;
using Game.Prefabs;
using StarQ.Shared.Extensions;
using static Game.Rendering.Debug.RenderPrefabRenderer;

namespace AssetDatabaseContributor.Systems
{
    public partial class ExtractionSystem : GameSystemBase
    {
        private readonly Dictionary<string, string> dlcVersions = new();

        private void GetDLCVersions()
        {
            dlcVersions.Clear();
            using IEnumerator<IDlc> enumerator2 = PlatformManager
                .instance.EnumerateLocalDLCs()
                .GetEnumerator();
            while (enumerator2.MoveNext())
            {
                IDlc dlc = enumerator2.Current;
                dlcVersions[dlc.internalName] = dlc.version.shortVersion;
            }
        }

        private void GetLocalesToExtract(
            PrefabBase prefabBase,
            string source,
            string sourceId,
            string sourceVersion,
            Dictionary<
                (string Source, string SourceId, string SourceVersion),
                List<(string Prefix, string Key)>
            > localesToExtract
        )
        {
            var key = (source, sourceId, sourceVersion);

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
                _ => 0,
            };
        }

        private static HashSet<(
            int index,
            string comp,
            string attr,
            string type,
            string note
        )> ComponentLib { get; set; } = new();

        //private HashSet<(
        //    int index,
        //    string comp,
        //    string attr,
        //    string type,
        //    string note
        //)> LoadExistingComponentLib()
        private void LoadExistingComponentLib()
        {
            //if (CachedComponentAttributes.Count > 0)
            //    return CachedComponentAttributes;

            //var result =
            //    new HashSet<(int index, string comp, string attr, string type, string note)>();

            Assembly? assembly = ModHelper.GetModExecutable(Mod.Id)?.assembly;

            if (assembly == null)
            {
                LogHelper.SendLog("Something went wrong, could not find own assembly");
                //return result;
                return;
            }

            //string path = Path.Combine(ModHelper.GetModPath(Mod.Instance), "components_attr.tsv");

            //if (!File.Exists(path))
            //    return result;

            //if (File.ReadLines(path).Count() == 0)
            //    return result;

            //using CSVReader reader = new(new StreamReader(path), GetCSVSetting());

            using Stream resource = assembly.GetManifestResourceStream(
                $"{Mod.Id}.EmbedCustom.components_attr.tsv.gz"
            );

            if (resource == null)
            {
                LogHelper.SendLog("Something went wrong, could not find own embedded TSV");
                //return result;
                return;
            }

            using GZipStream gzip = new(resource, CompressionMode.Decompress);
            using StreamReader streamReader = new(gzip);
            using CSVReader reader = new(streamReader, GetCSVSetting());

            List<ComponentAttributeRow> rows = reader.Deserialize<ComponentAttributeRow>();
            HashSet<string> validComps = new();

            foreach (ComponentAttributeRow row in rows)
            {
                //result.Add((row.index, row.comp_name, row.attr_name, row.attr_type, row.note));
                validComps.Add(row.comp_name);
            }

            //CachedComponentAttributes = result;

            ValidComponents = validComps.ToList();

            //return result;
        }

        //public static new HashSet<(
        //    int index,
        //    string comp,
        //    string attr,
        //    string type,
        //    string note
        //)> CachedComponentAttributes = new();

        public static List<string> ValidComponents = new();

        public static bool IsValidComponent(string typeName) => ValidComponents.Contains(typeName);

        //private void ValidateComponentLib()
        //{
        //    var existing = LoadExistingComponentLib();

        //    var existingGroups = existing
        //        .GroupBy(x => x.comp)
        //        .ToDictionary(g => g.Key, g => g.ToHashSet());

        //    var currentGroups = ComponentLib
        //        .GroupBy(x => x.comp)
        //        .ToDictionary(g => g.Key, g => g.ToHashSet());

        //    var filtered =
        //        new HashSet<(int index, string comp, string attr, string type, string note)>();

        //    foreach (var (comp, currentRows) in currentGroups)
        //    {
        //        if (!existingGroups.TryGetValue(comp, out var existingRows))
        //        {
        //            LogHelper.SendLog(
        //                $"Component '{comp}' is new and will be added to the component library."
        //            );
        //            filtered.UnionWith(currentRows);
        //            continue;
        //        }

        //        if (!currentRows.SetEquals(existingRows))
        //        {
        //            var currentByAttr = currentRows.ToDictionary(r => r.attr);
        //            var existingByAttr = existingRows.ToDictionary(r => r.attr);

        //            foreach (var attr in currentByAttr.Keys.Intersect(existingByAttr.Keys))
        //            {
        //                var current = currentByAttr[attr];
        //                var existin = existingByAttr[attr];

        //                if (current.type != existin.type || current.note != existin.note)
        //                {
        //                    LogHelper.SendLog(
        //                        $"Component '{comp}', attribute '{attr}' changed:\n"
        //                            + $"  Type: {existin.type} -> {current.type}\n"
        //                            + $"  Note: {existin.note} -> {current.note}",
        //                        LogLevel.Info
        //                    );
        //                }
        //            }

        //            filtered.UnionWith(currentRows);
        //        }
        //    }

        //    ComponentLib = filtered;
        //}

        internal static readonly HashSet<string> ImagesNeeded = new();

        internal static HashSet<string> ModsToCheck = new();
        internal static HashSet<string> ModsToReject = new();
        internal static HashSet<string> ModsAdded = new();

        public static FileSystemDataSource.PathEscapePolicy kPathEscapePolicy = new();
    }
}
