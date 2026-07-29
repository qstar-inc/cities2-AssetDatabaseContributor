using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.PSI.Environment;
using CSVFile;
using Game;
using Game.Prefabs;
using Newtonsoft.Json;
using StarQ.Shared.Extensions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace AssetDatabaseContributor.Systems
{
    public partial class ExtractionSystem : GameSystemBase
    {
        protected override void OnCreate() { }

        protected override void OnUpdate() { }

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

        public enum Limits
        {
            Game,
            Mod,
        }

        public enum Mode
        {
            Trigger,
            Task,
        }

        public void ExtractPrefabs(Limits limit)
        {
            if (limit == Limits.Mod && ModsToCheck.Count == 0)
            {
                LogHelper.SendLog($"No mods selected to check. Cancelling...");
                return;
            }

            ModsAdded.Clear();
            ImagesNeeded.Clear();
            ComponentLib.Clear();
            LoadExistingComponentLib();
            DateTime now = DateTime.UtcNow;
            LogHelper.SendLog($"Starting extraction at {now.ToLocalTime()}...", LogLevel.DEVD);

            string timeNow = $"{now:yyyy-MM-dd-HH-mm-ss}";

            //int i = 0;

            List<AssetDataExtract> assetData = new();
            Dictionary<
                (string Source, string SourceId, string SourceVersion),
                List<(string Prefix, string Key)>
            > localesToExtract = new();

            NativeArray<Entity> entities = SystemAPI
                .QueryBuilder()
                .WithAll<PrefabData>()
                .Build()
                .ToEntityArray(Allocator.Temp);

            GetDLCVersions();

            UnityObjectsMap? pmap = AssetDatabase.global.resources.prefabsMap;
            LogHelper.SendLog($"{entities.Length} entites found", LogLevel.DEVD);

            if (ModsToCheck.Count > 0)
            {
                LogHelper.SendLog(
                    $"Starting asset scan for mods: {string.Join(", ", ModsToCheck)}",
                    LogLevel.DEVD
                );
            }

            foreach (Entity entity in entities)
            {
                if (!WorldHelper.PrefabSystem.TryGetPrefab(entity, out PrefabBase prefabBase))
                    continue;

                if (prefabBase == null)
                    continue;

                if (limit == Limits.Game)
                {
                    if (!prefabBase.isBuiltin && prefabBase.asset == null)
                        continue;
                    if (
                        prefabBase.asset != null
                        && prefabBase.asset.path.Contains(EnvPath.kUserDataPath)
                    )
                        continue;
                }
                else if (limit == Limits.Mod)
                {
                    if (prefabBase.isBuiltin)
                        continue;

                    if (prefabBase.asset == null)
                        continue;
                }

                string subPath = string.Empty;
                string path = string.Empty;
                string guid = string.Empty;

                string source = "";
                string sourceId = "";
                string sourceVersion = "";

                if (prefabBase.asset != null)
                {
                    if (prefabBase.asset.subPath != null)
                        subPath = prefabBase.asset.subPath;

                    if (prefabBase.asset.path != null)
                        path = prefabBase.asset.path;

                    if (limit == Limits.Mod)
                    {
                        if (!path.Contains(EnvPath.kCacheDataPath))
                            continue;

                        if (!ModsToCheck.Any(s => subPath.Contains(s)))
                        {
                            //LogHelper.SendLog($"subPath ({subPath}) is rejected", LogLevel.DEVD);
                            continue;
                        }

                        string modText = subPath.Split("/")[1];
                        sourceId = modText.Split("_")[0];
                        sourceVersion = modText.Split("_")[1];
                        source = "Mod";
                    }

                    if (prefabBase.asset.id.guid != null)
                        guid = prefabBase.asset.id.guid.ToString();
                }

                if (subPath == "")
                {
                    source = "Game";
                    sourceId = "Game";
                }

                path = path.Replace(EnvPath.kCacheDataPath, "<m>")
                    .Replace(EnvPath.kContentPath, "<c>");

                if (subPath == "" && prefabBase.TryGet(out ContentPrerequisite cp))
                {
                    source = "DLC";
                    sourceId = cp.m_ContentPrerequisite.name;
                }
                sourceVersion = dlcVersions.ContainsKey(sourceId)
                    ? dlcVersions[sourceId]
                    : sourceVersion;

                if (guid == string.Empty)
                    pmap.TryGetGuid(prefabBase, out guid);

                string prefabType = prefabBase.GetType().Name;

                GetLocalesToExtract(prefabBase, source, sourceId, sourceVersion, localesToExtract);

                string sourceIdVersion = $"{sourceId}_{sourceVersion}";

                if (ModsToReject.Contains(sourceIdVersion))
                    continue;

                Dictionary<string, object> objects = new();

                Dictionary<string, object?>? pData = DumpObject(prefabBase, sourceIdVersion);
                if (pData == null)
                    continue;

                objects[prefabType] = pData;

                foreach (ComponentBase item in prefabBase.components)
                {
                    Dictionary<string, object?>? compData = DumpObject(item, sourceIdVersion);
                    if (compData == null)
                        continue;
                    string compName = item.GetType().Name;
                    objects[compName] = compData;
                }

                if (prefabBase.TryGet(out UIObject UIO))
                {
                    if (UIO != null && UIO.m_Icon != null && UIO.m_Icon != string.Empty)
                        ImagesNeeded.Add(UIO.m_Icon);
                }
                if (prefabBase.TryGet(out SignatureBuilding SGB))
                {
                    if (
                        SGB != null
                        && SGB.m_UnlockEventImage != null
                        && SGB.m_UnlockEventImage != string.Empty
                    )
                        ImagesNeeded.Add(SGB.m_UnlockEventImage);
                }

                assetData.Add(
                    new AssetDataExtract()
                    {
                        PrefabType = prefabType,
                        Name = prefabBase.name,
                        GUID = guid,
                        Components = objects,
                        SubPath = subPath,
                        Path = path,
                        Source = source,
                        SourceId = sourceId,
                        SourceVersion = sourceVersion,
                    }
                );
                ModsAdded.Add($"{sourceId}_{sourceVersion}");
                //sb.AppendLine($"{prefabType}:{prefabBase.name}");

                //if (found == true)
                //    i++;

                //if (i >= 10)
                //    break;
            }

            LogHelper.SendLog($"{assetData.Count} assets logged", LogLevel.DEVD);
            LogHelper.SendLog($"{ImagesNeeded.Count} icons needed", LogLevel.DEVD);

            string baseDirectory = $"{Mod.DataDir}\\~Extracted";
            Directory.CreateDirectory(baseDirectory);

            LogHelper.SendLog($"Sorting...", LogLevel.DEVD);

            List<AssetDataExtract>? items = assetData
                .OrderBy(x => x.Name ?? string.Empty)
                .ThenBy(x => x.GUID ?? string.Empty)
                .ToList();

            LogHelper.SendLog(
                $"Starting writing to zip files for {items.Count} items...",
                LogLevel.DEVD
            );

            foreach (var sourceGroup in items.GroupBy(x => x.Source))
            {
                var source = sourceGroup.Key;

                foreach (var sourceIdGroup in sourceGroup.GroupBy(x => x.SourceId))
                {
                    var sourceId = sourceIdGroup.Key;

                    foreach (var versionGroup in sourceIdGroup.GroupBy(x => x.SourceVersion))
                    {
                        var sourceVersion = versionGroup.Key;

                        LogHelper.SendLog(
                            $"==================================================",
                            LogLevel.DEVD
                        );
                        LogHelper.SendLog(
                            $"Zipping for {source}/{sourceId}/{sourceVersion}...",
                            LogLevel.DEVD
                        );

                        List<PrefabRow> prefabs = new();
                        List<ComponentRow> components = new();

                        int c = 0;
                        foreach (AssetDataExtract item in versionGroup)
                        {
                            prefabs.Add(
                                new PrefabRow
                                {
                                    guid = item.GUID,
                                    name = item.Name,
                                    prefab_type = item.PrefabType,
                                    sub_path = item.SubPath,
                                    path = item.Path,
                                }
                            );

                            foreach (var comp in item.Components)
                            {
                                var attrVal = JsonConvert.SerializeObject(
                                    comp.Value,
                                    GetJsonSetting()
                                );

                                //if (item.GUID.ToString() == "39980c750d4c50b4ea7a7477b0411513")
                                //{
                                //    try
                                //    {
                                //        LogHelper.SendLog(comp.Value.ToString());
                                //    }
                                //    catch { }
                                //    LogHelper.SendLog(attrVal);
                                //}

                                c++;
                                components.Add(
                                    new ComponentRow
                                    {
                                        guid = item.GUID,
                                        attr_id = comp.Key,
                                        attr_value = attrVal,
                                    }
                                );
                            }
                        }

                        string zipPath = Path.Combine(
                            baseDirectory,
                            $"~ADC_{timeNow}_{source}_{sourceId}_{sourceVersion}.zip"
                        );

                        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

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
                            $"Wrote {versionGroup.Count()} prefabs to 'prefabs.tsv'",
                            LogLevel.DEVD
                        );
                        LogHelper.SendLog(
                            $"Wrote {c} components to 'components.tsv'",
                            LogLevel.DEVD
                        );

                        #region locale extraction
                        LocalizationManager locMan = LocaleHelper.localizationManager;

                        HashSet<LocaleData> localeData = new();
                        string currentLang = locMan.activeLocaleId;
                        foreach (string lang in locMan.GetSupportedLocales())
                        {
                            locMan?.SetActiveLocale(lang);

                            if (
                                localesToExtract.TryGetValue(
                                    (source, sourceId, sourceVersion),
                                    out var localesToExport
                                )
                            )
                            {
                                foreach ((string prefix, string key) in localesToExport)
                                {
                                    string getId = $"{prefix}[{key}]";
                                    if (
                                        locMan?.activeDictionary?.TryGetValue(
                                            getId,
                                            out string result
                                        ) == true
                                        && (result != getId)
                                    )
                                    {
                                        localeData.Add(
                                            new LocaleData()
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

                        locMan?.SetActiveLocale(currentLang);

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

                            IOrderedEnumerable<LocaleData> sorted = localeData
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
                                        prefix = row.Prefix,
                                        name = row.Name,
                                        lang = row.Lang,
                                        text = row.Text,
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
                    }
                }
            }

            //#region comp_attr extraction
            //ValidateComponentLib();
            //if (ComponentLib.Count > 0)
            //{
            //    LogHelper.SendLog(
            //        $"==================================================",
            //        LogLevel.DEVD
            //    );
            //    string zipPath = Path.Combine(baseDirectory, $"~ADC_{timeNow}_compAttr.zip");
            //    using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            //    using StreamWriter wrt_ca = new(
            //        archive
            //            .CreateEntry(
            //                "components_attr.tsv",
            //                System.IO.Compression.CompressionLevel.Optimal
            //            )
            //            .Open()
            //    );

            //    var sorted = ComponentLib
            //        .OrderBy(x => x.comp)
            //        .ThenBy(x => x.index)
            //        .ThenBy(x => x.attr)
            //        .ThenBy(x => x.type);

            //    List<ComponentAttributeRow> componentAttributeRow = new();
            //    foreach (var (index, comp, attr, type, note) in sorted)
            //    {
            //        componentAttributeRow.Add(
            //            new ComponentAttributeRow
            //            {
            //                comp_name = comp,
            //                index = index,
            //                attr_name = attr,
            //                attr_type = type,
            //                note = note,
            //            }
            //        );
            //    }

            //    wrt_ca.Write(CSV.Serialize(componentAttributeRow, GetCSVSetting()));

            //    //foreach (var (index, comp, attr, type, note) in sorted)
            //    //{
            //    //    wrt_ca.Write(
            //    //        new object[] { comp, index, attr, type, note }.ToCSVString(GetCSVSetting())
            //    //    );
            //    //}

            //    LogHelper.SendLog(
            //        $"Wrote {ComponentLib.Count()} component attributes to 'components_attr.tsv'",
            //        LogLevel.DEVD
            //    );
            //}
            //#endregion comp_attr extraction

            LogHelper.SendLog($"==================================================", LogLevel.DEVD);
        }

        public static Dictionary<string, object?>? DumpObject(
            ComponentBase obj,
            string sourceIdVersion
        )
        {
            Dictionary<string, object?>? result = new() { };

            if (obj is not PrefabBase)
                result["name"] = obj.name;

            BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            int i = 0;

            Type type = obj.GetType();

            bool valid = IsValidComponent(type.Name);
            if (!valid)
            {
                ModsToReject.Add(sourceIdVersion);
                LogHelper.SendLog(
                    $"{sourceIdVersion} got rejected because it contains {type.Name} component"
                );
                return null;
            }

            ComponentLib.Add((i++, type.Name, "", "", ""));
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.Name is "active" or "version" or "components" or "isDirty")
                    continue;

                string note = field.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? "";
                ComponentLib.Add((i++, type.Name, field.Name, field.FieldType.ToString(), note));

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

            if (value is PrefabBase prefab)
            {
                if (AssetDatabase.global.resources.prefabsMap.TryGetGuid(prefab, out string id))
                    return $"GUID:{id}";

                if (prefab.asset != null)
                    return $"CID:{prefab.asset.id.guid}";

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
                    return $"UOM:{guid}";

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
