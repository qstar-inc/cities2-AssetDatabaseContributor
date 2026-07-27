using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Common;
using CSVFile;
using Game;
using Game.Prefabs;
using StarQ.Shared.Extensions;

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

        private HashSet<(
            int index,
            string comp,
            string attr,
            string type,
            string note
        )> LoadExistingComponentLib()
        {
            var result =
                new HashSet<(int index, string comp, string attr, string type, string note)>();

            string path = Path.Combine(ModHelper.GetModPath(Mod.Instance), "components_attr.tsv");

            if (!File.Exists(path))
                return result;

            if (File.ReadLines(path).Count() == 0)
                return result;

            using CSVReader reader = new(new StreamReader(path), GetCSVSetting());
            List<ComponentAttributeRow> rows = reader.Deserialize<ComponentAttributeRow>();

            foreach (ComponentAttributeRow row in rows)
                result.Add((row.index, row.comp_name, row.attr_name, row.attr_type, row.note));

            return result;
        }

        private void ValidateComponentLib()
        {
            var existing = LoadExistingComponentLib();

            var existingGroups = existing
                .GroupBy(x => x.comp)
                .ToDictionary(g => g.Key, g => g.ToHashSet());

            var currentGroups = ComponentLib
                .GroupBy(x => x.comp)
                .ToDictionary(g => g.Key, g => g.ToHashSet());

            var filtered =
                new HashSet<(int index, string comp, string attr, string type, string note)>();

            foreach (var (comp, currentRows) in currentGroups)
            {
                if (!existingGroups.TryGetValue(comp, out var existingRows))
                {
                    LogHelper.SendLog(
                        $"Component '{comp}' is new and will be added to the component library."
                    );
                    filtered.UnionWith(currentRows);
                    continue;
                }

                if (!currentRows.SetEquals(existingRows))
                {
                    var currentByAttr = currentRows.ToDictionary(r => r.attr);
                    var existingByAttr = existingRows.ToDictionary(r => r.attr);

                    foreach (var attr in currentByAttr.Keys.Intersect(existingByAttr.Keys))
                    {
                        var current = currentByAttr[attr];
                        var existin = existingByAttr[attr];

                        if (current.type != existin.type || current.note != existin.note)
                        {
                            LogHelper.SendLog(
                                $"Component '{comp}', attribute '{attr}' changed:\n"
                                    + $"  Type: {existin.type} -> {current.type}\n"
                                    + $"  Note: {existin.note} -> {current.note}",
                                LogLevel.Info
                            );
                        }
                    }

                    filtered.UnionWith(currentRows);
                }
            }

            ComponentLib = filtered;
        }

        internal static readonly HashSet<string> ImagesNeeded = new();

        internal static HashSet<string> ModsToCheck = new();
        internal static HashSet<string> ModsAdded = new();

        public static FileSystemDataSource.PathEscapePolicy kPathEscapePolicy = new();
    }
}
