using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.OdinSerializer;
using Colossal.PSI.Common;
using CSVFile;
using Game;
using Game.Prefabs;
using StarQ.Shared.Extensions;
using UnityEngine;

namespace AssetDatabaseContributor.Systems
{
    public partial class ExtractionSystem : GameSystemBase
    {
        private static HashSet<(
            int index,
            string comp,
            string attr,
            string type,
            string note
        )> ComponentLib { get; set; } = new();

        private readonly Dictionary<string, string> dlcVersions = new();

        internal static readonly HashSet<string> ImagesNeeded = new();

        internal static HashSet<string> ModsToCheck = new();
        internal static HashSet<string> ModsToReject = new();
        internal static HashSet<string> ModsAdded = new();
        public static List<string> ValidComponents = new();

        public static FileSystemDataSource.PathEscapePolicy kPathEscapePolicy = new();

        public static bool IsValidComponent(string typeName) => ValidComponents.Contains(typeName);

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

        private void LoadExistingComponentLib()
        {
            Assembly? assembly = ModHelper.GetModExecutable(Mod.Id)?.assembly;

            if (assembly == null)
            {
                LogHelper.SendLog("Something went wrong, could not find own assembly");
                return;
            }

            using Stream resource = assembly.GetManifestResourceStream(
                $"{Mod.Id}.EmbedCustom.components_attr.tsv.gz"
            );

            if (resource == null)
            {
                LogHelper.SendLog("Something went wrong, could not find own embedded TSV");
                return;
            }

            using GZipStream gzip = new(resource, CompressionMode.Decompress);
            using StreamReader streamReader = new(gzip);
            using CSVReader reader = new(streamReader, GetCSVSetting());

            List<ComponentAttributeRow> rows = reader.Deserialize<ComponentAttributeRow>();
            HashSet<string> validComps = new();

            foreach (ComponentAttributeRow row in rows)
                validComps.Add(row.comp_name);

            ValidComponents = validComps.ToList();
        }

        public void TestPrefabs()
        {
            WorldHelper.PrefabSystem.TryGetPrefab(
                new PrefabID(
                    nameof(TrackPrefab),
                    "W7Double Train Track - Station Middle",
                    Colossal.Hash128.Parse("b1fe85b77295966146e41d9f6da6fe65")
                ),
                out PrefabBase pb
            );

            if (!PrefabHelper.TryGetOriginal(pb, out PrefabBase original))
            {
                LogHelper.SendLog("so is null");
                return;
            }

            if (original.GetType() == pb.GetType())
            {
                if (pb.TryGet(out UIObject uio))
                    LogHelper.SendLog($"UIO Icon is {uio.m_Icon}");
                else
                    LogHelper.SendLog($"No UIO Icon in pb");
                if (original.TryGet(out UIObject uio2))
                    LogHelper.SendLog($"UIO Icon is {uio2.m_Icon}");
                else
                    LogHelper.SendLog($"No UIO Icon in so");
            }

            if (pb.asset == original.asset)
                LogHelper.SendLog("pb.asset == original.asset");
            else
                LogHelper.SendLog("pb.asset != original.asset");
            if (pb.components == original.components)
                LogHelper.SendLog("pb.components == original.components");
            else
                LogHelper.SendLog("pb.components != original.components");
        }
    }
}
