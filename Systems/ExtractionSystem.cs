using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using AssetDatabaseContributor.Types;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Environment;
using Colossal.UI;
using CSVFile;
using Game;
using Game.Assets;
using Game.Prefabs;
using Newtonsoft.Json;
using StarQ.Shared.Extensions;
using UnityEngine;

namespace AssetDatabaseContributor.Systems
{
    public partial class ExtractionSystem : GameSystemBase
    {
        protected override void OnCreate() { }

        protected override void OnUpdate() { }

        public void ExtractData()
        {
            if (ModsToCheck.Count == 0)
            {
                LogHelper.SendLog($"No mods selected to check. Cancelling...");
                return;
            }

            ModsAdded.Clear();
            ImagesNeeded.Clear();
            //LoadExistingComponentLib();
            DateTime now = DateTime.UtcNow;
            LogHelper.SendLog(
                $"Starting prefab extraction at {now.ToLocalTime()}...",
                LogLevel.DEVD
            );

            string timeNow = $"{now:yyyy-MM-dd-HH-mm-ss}";

            List<PrefabDataExtract> assetData = new();
            List<MapDataExtract> mapDataExtracts = new();
            Dictionary<
                (string SourceId, string SourceVersion),
                List<(string Prefix, string Key)>
            > localesToExtract = new();

            UnityObjectsMap? pmap = AssetDatabase.global.resources.prefabsMap;

            #region prefab extraction
            foreach (
                PrefabAsset prefabAsset in AssetDatabase.global.GetAssets<PrefabAsset>(default)
            )
            {
                if (prefabAsset.database is not AssetDatabase<ParadoxMods>)
                    continue;

                PrefabBase prefabBase = prefabAsset.GetInstance<PrefabBase>();

                //if (!WorldHelper.PrefabSystem.TryGetPrefab(entity, out PrefabBase prefabBase))
                //    continue;

                //if (prefabBase.name.Contains(assetToFind))
                //    LogHelper.SendLog($"PrefabBase found for {prefabBase.name}", LogLevel.DEVD);

                if (
                    LogHelper.CheckNull(
                        prefabBase,
                        "PrefabBase is null",
                        "This should never happen",
                        LogLevel.Info
                    )
                )
                    continue;

                PrefabBase compPrefabBase = prefabBase;

                string subPath = string.Empty;
                string path = string.Empty;
                string guid = string.Empty;

                string sourceId = "";
                string sourceVersion = "";

                if (prefabBase.asset.subPath != null)
                    subPath = prefabBase.asset.subPath;

                if (prefabBase.asset.path != null)
                    path = prefabBase.asset.path;

                if (!ModsToCheck.Any(s => subPath.Contains(s)))
                    continue;

                if (!path.Contains(EnvPath.kCacheDataPath))
                    continue;

                if (
                    PrefabHelper.TryGetOriginal(prefabBase, out PrefabBase? noModPb)
                    && noModPb != null
                )
                    compPrefabBase = noModPb;

                if (noModPb == null)
                    continue;

                string modText = subPath.Split("/")[1];
                sourceId = modText.Split("_")[0];
                sourceVersion = modText.Split("_")[1];

                if (prefabBase.asset.id.guid != null)
                    guid = prefabBase.asset.id.guid.ToString();

                path = path.Replace($"{EnvPath.kCacheDataPath}/Mods/{subPath}", "<m>");

                if (guid == string.Empty)
                    pmap.TryGetGuid(prefabBase, out guid);

                string prefabType = prefabBase.GetType().Name;

                GetModLocalesToExtract(prefabBase, sourceId, sourceVersion, localesToExtract);

                string sourceIdVersion = $"{sourceId}_{sourceVersion}";

                if (ModsToReject.Contains(sourceIdVersion))
                    continue;

                if (compPrefabBase.components.Capacity != compPrefabBase.components.Count)
                {
                    LogHelper.SendLog(
                        $"{prefabType}:{prefabBase.name} has capacity {compPrefabBase.components.Capacity} but count {compPrefabBase.components.Count}. Rejecting..."
                    );
                    ModsToReject.Add(sourceIdVersion);
                    continue;
                }

                Dictionary<string, object> objects = new();

                Dictionary<string, object?>? pData = DumpObject(compPrefabBase);
                if (pData == null)
                    continue;

                objects[prefabType] = pData;

                foreach (ComponentBase item in compPrefabBase.components)
                {
                    Dictionary<string, object?>? compData = DumpObject(item);
                    if (compData == null)
                        continue;

                    string compName = item.GetType().Name;
                    objects[compName] = compData;
                }

                if (compPrefabBase.TryGet(out UIObject UIO))
                {
                    if (UIO != null && UIO.m_Icon != null && UIO.m_Icon != string.Empty)
                        ImagesNeeded.Add(UIO.m_Icon);
                }

                if (compPrefabBase.TryGet(out SignatureBuilding SGB))
                {
                    if (
                        SGB != null
                        && SGB.m_UnlockEventImage != null
                        && SGB.m_UnlockEventImage != string.Empty
                    )
                        ImagesNeeded.Add(SGB.m_UnlockEventImage);
                }

                assetData.Add(
                    new PrefabDataExtract()
                    {
                        PrefabType = prefabType,
                        Name = prefabBase.name,
                        GUID = guid,
                        Components = objects,
                        SubPath = subPath,
                        Path = path,
                        SourceId = sourceId,
                        SourceVersion = sourceVersion,
                    }
                );
                ModsAdded.Add($"{sourceId}_{sourceVersion}");
            }
            #endregion prefab extraction

            #region map extraction
            foreach (
                MapMetadata mapMetadata in AssetDatabase.global.GetAssets<MapMetadata>(default)
            )
            {
                if (mapMetadata == null)
                    continue;

                if (mapMetadata.target.mapData == null)
                    continue;

                if (mapMetadata.database is not AssetDatabase<ParadoxMods>)
                    continue;

                if (mapMetadata.isBuiltin)
                    continue;

                mapMetadata.target.metaData.Load();

                if (mapMetadata.target.thumbnail == null)
                    continue;

                ImagesNeeded.Add(mapMetadata.target.thumbnail.ToUri());
                ImagesNeeded.Add(mapMetadata.target.preview.ToUri());

                string subPath = string.Empty;
                string path = string.Empty;
                string guid = string.Empty;
                string guidMeta = string.Empty;

                string sourceId = "";
                string sourceVersion = "";

                if (mapMetadata.subPath != null)
                    subPath = mapMetadata.subPath;

                if (mapMetadata.path != null)
                    path = mapMetadata.path;

                if (!ModsToCheck.Any(s => subPath.Contains(s)))
                    continue;

                if (!path.Contains(EnvPath.kCacheDataPath))
                    continue;

                string modText = subPath.Split("/")[1];
                sourceId = modText.Split("_")[0];
                sourceVersion = modText.Split("_")[1];

                string sourceIdVersion = $"{sourceId}_{sourceVersion}";
                if (ModsToReject.Contains(sourceIdVersion))
                    continue;

                if (mapMetadata.id.guid != null)
                    guid = mapMetadata.target.mapData.id.guid.ToString();
                if (mapMetadata.id.guid != null)
                    guidMeta = mapMetadata.id.guid.ToString();

                path = path.Replace(EnvPath.kCacheDataPath, "<m>");

                GetMapLocalesToExtract(
                    mapMetadata.target.displayName,
                    sourceId,
                    sourceVersion,
                    localesToExtract
                );

                mapDataExtracts.Add(
                    new MapDataExtract()
                    {
                        Name = mapMetadata.target.displayName,
                        GUID = guid,
                        GUIDMeta = guidMeta,
                        SubPath = subPath,
                        Path = path,
                        SourceId = sourceId,
                        SourceVersion = sourceVersion,
                        Metadata = mapMetadata.target,
                    }
                );
                ModsAdded.Add(sourceIdVersion);
            }
            #endregion map extraction


            if (assetData.Count <= 0 && mapDataExtracts.Count <= 0 && ImagesNeeded.Count <= 0)
            {
                LogHelper.SendLog($"Nothing to collect", LogLevel.DEVD);
                return;
            }

            LogHelper.SendLog($"{assetData.Count} assets logged", LogLevel.DEVD);
            LogHelper.SendLog($"{mapDataExtracts.Count} map data logged", LogLevel.DEVD);
            LogHelper.SendLog($"{ImagesNeeded.Count} icons needed", LogLevel.DEVD);

            string baseDirectory = $"{Mod.DataDir}\\~Extracted";
            Directory.CreateDirectory(baseDirectory);

            LogHelper.SendLog($"Sorting...", LogLevel.DEVD);

            List<PrefabDataExtract>? prefabItems = assetData
                .Where(x => !ModsToReject.Contains($"{x.SourceId}_{x.SourceVersion}"))
                .OrderBy(x => x.Name ?? string.Empty)
                .ThenBy(x => x.GUID ?? string.Empty)
                .ToList();
            List<MapDataExtract>? items = mapDataExtracts
                .Where(x => !ModsToReject.Contains($"{x.SourceId}_{x.SourceVersion}"))
                .OrderBy(x => x.Name ?? string.Empty)
                .ThenBy(x => x.GUID ?? string.Empty)
                .ToList();

            LogHelper.SendLog(
                $"Starting writing to package files for {prefabItems.Count} prefabs and {items.Count} maps...",
                LogLevel.DEVD
            );

            var mapsByKey = items
                .GroupBy(x => (x.SourceId, x.SourceVersion))
                .ToDictionary(g => g.Key, g => g.ToList());

            var processedKeys = new HashSet<(string SourceId, string SourceVersion)>();

            #region processor method
            void ProcessKey(
                string sourceId,
                string sourceVersion,
                List<PrefabDataExtract> prefabsForKey
            )
            {
                LogHelper.SendLog(
                    $"==================================================",
                    LogLevel.DEVD
                );
                LogHelper.SendLog(
                    $"Packaging for Mod/{sourceId}/{sourceVersion}...",
                    LogLevel.DEVD
                );

                List<PrefabRow> prefabs = new();
                List<ComponentRow> components = new();

                int componentCount = 0;
                foreach (PrefabDataExtract item in prefabsForKey)
                {
                    prefabs.Add(
                        new PrefabRow
                        {
                            GUID = item.GUID,
                            Name = item.Name,
                            PrefabType = item.PrefabType,
                            SubPath = item.SubPath,
                            Path = item.Path,
                        }
                    );

                    foreach (var comp in item.Components)
                    {
                        var attrVal = JsonConvert.SerializeObject(comp.Value, GetJsonSetting());

                        componentCount++;
                        components.Add(
                            new ComponentRow
                            {
                                GUID = item.GUID,
                                AttrID = comp.Key,
                                AttrValue = attrVal,
                            }
                        );
                    }
                }

                string zipPath = Path.Combine(
                    baseDirectory,
                    $"~{timeNow}_Mod_{sourceId}_{sourceVersion}.adc"
                );

                using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                if (prefabsForKey.Count > 0)
                {
                    var entry = archive.CreateEntry(
                        "prefabs.tsv",
                        System.IO.Compression.CompressionLevel.Optimal
                    );

                    using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write(CSV.Serialize(prefabs, GetCSVSetting()));
                    }

                    entry = archive.CreateEntry(
                        "components.tsv",
                        System.IO.Compression.CompressionLevel.Optimal
                    );
                    using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write(CSV.Serialize(components, GetCSVSetting()));
                    }

                    LogHelper.SendLog(
                        $"Wrote {prefabsForKey.Count} prefabs to 'prefabs.tsv'",
                        LogLevel.DEVD
                    );
                    LogHelper.SendLog(
                        $"Wrote {componentCount} components to 'components.tsv'",
                        LogLevel.DEVD
                    );
                }

                if (mapsByKey.TryGetValue((sourceId, sourceVersion), out var mapsForThisKey))
                {
                    List<MapDataRow> mapDatas = new();
                    foreach (MapDataExtract item in mapsForThisKey)
                    {
                        MapInfo metadata = item.Metadata;

                        if (
                            !AssetHelper.TryGetAssetGUID(
                                metadata.thumbnail.ToUri(),
                                out string thumbGUID
                            )
                        )
                            continue;
                        if (
                            !AssetHelper.TryGetAssetGUID(
                                metadata.preview.ToUri(),
                                out string prevGUID
                            )
                        )
                            continue;

                        mapDatas.Add(
                            new MapDataRow
                            {
                                GUID = item.GUID,
                                MetadataGUID = item.GUIDMeta,
                                ThumbnailGUID = thumbGUID,
                                PreviewGUID = prevGUID,
                                Name = item.Name,
                                SubPath = item.SubPath,
                                Path = item.Path,
                                Theme = metadata.theme,
                                TempMin = metadata.temperatureRange.min,
                                TempMax = metadata.temperatureRange.max,
                                Cloudiness = metadata.cloudiness,
                                Precipitation = metadata.precipitation,
                                Latitude = metadata.latitude,
                                Longitude = metadata.longitude,
                                BuildableLand = metadata.buildableLand,
                                Area = metadata.area,
                                SurfaceWaterAvailability = metadata.surfaceWaterAvailability,
                                GroundWaterAvailability = metadata.groundWaterAvailability,
                                ResFertile = metadata.resources.fertile,
                                ResForest = metadata.resources.forest,
                                ResOil = metadata.resources.oil,
                                ResOre = metadata.resources.ore,
                                ResFish = metadata.resources.fish,
                                ConnRoad = metadata.connections.road ? 1 : 0,
                                ConnTrain = metadata.connections.train ? 1 : 0,
                                ConnAir = metadata.connections.air ? 1 : 0,
                                ConnShip = metadata.connections.ship ? 1 : 0,
                                ConnElectricity = metadata.connections.electricity ? 1 : 0,
                                ConnWater = metadata.connections.water ? 1 : 0,
                                ContentPrerequisites =
                                    metadata.contentPrerequisites != null
                                        ? string.Join(",", metadata.contentPrerequisites)
                                        : string.Empty,
                                NameAsCityName = metadata.nameAsCityName ? 1 : 0,
                                StartingYear = metadata.startingYear,
                            }
                        );
                    }

                    var mapEntry = archive.CreateEntry(
                        "maps.tsv",
                        System.IO.Compression.CompressionLevel.Optimal
                    );
                    using (var writer = new StreamWriter(mapEntry.Open(), new UTF8Encoding(false)))
                        writer.Write(CSV.Serialize(mapDatas, GetCSVSetting()));

                    LogHelper.SendLog($"Wrote {mapDatas.Count} maps to 'maps.tsv'", LogLevel.DEVD);
                }

                #region locale zipping
                HashSet<LocaleRowData> localeData = new();
                Dictionary<string, List<LocaleAsset>> assetsByLocale = new();
                foreach (LocaleAsset asset in AssetDatabase.global.GetAssets<LocaleAsset>(default))
                {
                    string id = asset.localeId;
                    if (id == null)
                        continue;
                    if (!assetsByLocale.TryGetValue(id, out var list))
                    {
                        list = new List<LocaleAsset>();
                        assetsByLocale[id] = list;
                    }
                    list.Add(asset);
                }

                if (
                    localesToExtract.TryGetValue((sourceId, sourceVersion), out var localesToExport)
                )
                {
                    foreach (var (lang, assets) in assetsByLocale)
                    {
                        Dictionary<string, string> rawEntries = new(StringComparer.Ordinal);
                        Dictionary<string, int> indexCounts = new(StringComparer.Ordinal);
                        List<IDictionaryEntryError> errors = new();

                        foreach (LocaleAsset asset in assets)
                        {
                            foreach (
                                KeyValuePair<string, string> kv in asset.ReadEntries(
                                    errors,
                                    indexCounts
                                )
                            )
                                rawEntries[kv.Key] = kv.Value;
                        }

                        foreach ((string prefix, string key) in localesToExport)
                        {
                            string getId = $"{prefix}[{key}]";
                            if (rawEntries.TryGetValue(getId, out string result) && result != getId)
                            {
                                localeData.Add(
                                    new LocaleRowData()
                                    {
                                        Prefix = GetLocalePrefixId(prefix),
                                        Name = key,
                                        Lang = lang,
                                        Text = result,
                                    }
                                );
                            }
                        }
                    }
                }

                if (localeData.Count > 0)
                {
                    using StreamWriter wrt_loc = new(
                        archive
                            .CreateEntry(
                                "locale.tsv",
                                System.IO.Compression.CompressionLevel.Optimal
                            )
                            .Open()
                    );

                    IOrderedEnumerable<LocaleRowData> sorted = localeData
                        .OrderBy(x => x.Name)
                        .ThenBy(x => x.Prefix)
                        .ThenBy(x => x.Lang)
                        .ThenBy(x => x.Text);

                    List<LocaleRow> localeRows = new();
                    foreach (var row in sorted)
                    {
                        localeRows.Add(
                            new LocaleRow
                            {
                                Prefix = row.Prefix,
                                Name = row.Name,
                                Lang = row.Lang,
                                Text = row.Text,
                            }
                        );
                    }

                    wrt_loc.Write(CSV.Serialize(localeRows, GetCSVSetting()));

                    LogHelper.SendLog(
                        $"Wrote {localeData.Count()} locale entries to 'locale.tsv'",
                        LogLevel.DEVD
                    );
                }

                #endregion locale extraction
                processedKeys.Add((sourceId, sourceVersion));
            }
            #endregion processor method


            foreach (var sourceIdGroup in prefabItems.GroupBy(x => x.SourceId))
            {
                foreach (var versionGroup in sourceIdGroup.GroupBy(x => x.SourceVersion))
                {
                    ProcessKey(sourceIdGroup.Key, versionGroup.Key, versionGroup.ToList());
                }
            }

            foreach (var key in mapsByKey.Keys)
            {
                if (processedKeys.Contains(key))
                    continue;

                ProcessKey(key.SourceId, key.SourceVersion, new List<PrefabDataExtract>());
            }

            #region comp_attr extraction
            if (ComponentLib.Count > 0)
            {
                var newOrUpdated = ComponentLib.Except(BaseComponent).ToList();

                if (newOrUpdated.Count == 0)
                {
                    LogHelper.SendLog(
                        "No new or updated components found; skipping CSV write.",
                        LogLevel.DEVD
                    );
                }
                else
                {
                    LogHelper.SendLog(
                        $"==================================================",
                        LogLevel.DEVD
                    );
                    string zipPath = Path.Combine(baseDirectory, $"~{timeNow}_compAttr.adc");
                    using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                    using StreamWriter wrt_ca = new(
                        archive
                            .CreateEntry(
                                "components_attr.tsv",
                                System.IO.Compression.CompressionLevel.Optimal
                            )
                            .Open()
                    );

                    var sorted = newOrUpdated
                        .OrderBy(x => x.source)
                        .ThenBy(x => x.comp)
                        .ThenBy(x => x.index)
                        .ThenBy(x => x.attr)
                        .ThenBy(x => x.type);

                    List<ComponentAttributeRow> componentAttributeRow = new();
                    foreach (var (index, comp, attr, type, note, source) in sorted)
                    {
                        componentAttributeRow.Add(
                            new ComponentAttributeRow
                            {
                                CompName = comp,
                                Index = index,
                                AttrName = attr,
                                AttrType = type,
                                Note = note,
                                Source = source,
                            }
                        );
                    }

                    wrt_ca.Write(CSV.Serialize(componentAttributeRow, GetCSVSetting()));

                    LogHelper.SendLog(
                        $"Wrote {componentAttributeRow.Count} component attributes to 'components_attr.tsv'",
                        LogLevel.DEVD
                    );
                }
            }
            #endregion comp_attr extraction

            LogHelper.SendLog($"==================================================", LogLevel.DEVD);
        }

        public static Dictionary<string, object?>? DumpObject(ComponentBase obj)
        {
            Dictionary<string, object?>? result = new() { };

            if (obj is not PrefabBase)
                result["name"] = obj.name;

            BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            int i = 0;

            Type type = obj.GetType();

            ComponentLib.Add(
                (i++, type.Name, "", "", "", type.Assembly.GetName().Name ?? "Unknown")
            );
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.Name is "active" or "version" or "components" or "isDirty")
                    continue;

                string note = field.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? "";
                ComponentLib.Add(
                    (
                        i++,
                        type.Name,
                        field.Name,
                        field.FieldType.ToString(),
                        note,
                        field.DeclaringType?.Assembly.GetName().Name ?? "Unknown"
                    )
                );

                try
                {
                    result[field.Name] = DumpValue(
                        $"{obj.name}=>{field.Name}",
                        field.GetValue(obj)
                    );
                }
                catch (Exception e)
                {
                    result[field.Name] = "<unreadable>";
                    LogHelper.SendLog(
                        $"Error dumping field '{field.Name}' of '{obj.name}': {e.Message}",
                        LogLevel.DEVD
                    );
                }
            }

            return result;
        }

        private static object? DumpValue(string name, object? value, int depth = 0)
        {
            if (depth > 10)
                return "<max_depth>";
            depth++;

            if (value == null)
            {
                //LogHelper.SendLog($"{name} returning null");
                return null;
            }

            if (value is IntPtr or UIntPtr)
                return "<IntPtr>";

            if (value is PrefabBase prefab)
            {
                if (AssetDatabase.global.resources.prefabsMap.TryGetGuid(prefab, out string id))
                    return $"{id}";

                if (prefab.asset != null)
                    return $"{prefab.asset.id.guid}";

                return $"<{prefab.GetType().Name}:{prefab.name}>";
            }

            if (value is UnityEngine.Object unityObj)
            {
                if (
                    AssetDatabase.global.resources.unityObjectsMap.TryGetGuid(
                        unityObj,
                        out string guid
                    )
                )
                    return $"{guid}";

                if (AssetDatabase.global.resources.unityObjectsMap)
                    return $"<{unityObj.GetType().Name}>";
            }

            Type t = value.GetType();

            if (t.IsPrimitive || value is string || value is decimal || value is Enum)
                return value;

            if (value is IList list)
            {
                List<object?> listResult = new(list.Count);

                foreach (var item in list)
                    listResult.Add(DumpValue(name, item, depth));

                return listResult;
            }

            Dictionary<string, object?> result = new();

            foreach (
                FieldInfo field in t.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                )
            )
            {
                try
                {
                    result[field.Name] = DumpValue(name, field.GetValue(value), depth);
                }
                catch (Exception e)
                {
                    result[field.Name] = "<unreadable>";
                    LogHelper.SendLog(
                        $"Error dumping field '{field.Name}' of '{value?.GetType()?.Name}': {e.Message}",
                        LogLevel.DEVD
                    );
                }
            }

            return result;
        }

        private static void Validate(object? obj, string path = "")
        {
            //for (int n = 1; n <= allData.Assets.Count; n++)
            //{
            //    var item = allData.Assets[n];
            //    LogHelper.SendLog($"Trying {n}: {item.PrefabType}:{item.Name}");

            //    JsonConvert.SerializeObject(item, GetSetting());

            //    LogHelper.SendLog($"OK {n}");
            //}
            //----
            //if (obj == null)
            //    return;

            //switch (obj)
            //{
            //    case string:
            //    case bool:
            //    case byte:
            //    case sbyte:
            //    case short:
            //    case ushort:
            //    case int:
            //    case uint:
            //    case long:
            //    case ulong:
            //    case float:
            //    case double:
            //    case decimal:
            //    case Enum:
            //        return;

            //    case IDictionary dict:
            //        foreach (DictionaryEntry kv in dict)
            //            Validate(kv.Value, $"{path}/{kv.Key}");
            //        return;

            //    case IEnumerable list when obj is not string:
            //        int i = 0;
            //        foreach (var item in list)
            //            Validate(item, $"{path}[{i++}]");
            //        return;
            //}

            //Type t = obj.GetType();

            //LogHelper.SendLog($"ESCAPED: {path} -> {t.FullName}");
        }
    }
}
