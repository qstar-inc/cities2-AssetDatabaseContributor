using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AssetDatabaseContributor.Types;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Common;
using Colossal.Serialization.Entities;
using Game;
using Game.PSI;
using Game.SceneFlow;
using Game.UI.Menu;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PDX.SDK.Contracts.Service.Mods.Enums;
using StarQ.Shared.Extensions;
using StarQ.Shared.Types;
using static AssetDatabaseContributor.Systems.ExtractionSystem;
using File = System.IO.File;

namespace AssetDatabaseContributor.Systems
{
    public partial class StartupSystem : GameSystemBase
    {
        public static string notifIdentifier = $"{Mod.Id}.Notif";
        private static bool FirstMethodRan = false;
        private static bool FirstLoopDone = false;
        internal static bool TaskRunning = false;
        private static bool DisabledTemporarily = false;
        private static bool DisabledForSession = false;
        private static bool Cancelled = false;

        //#if DEBUG
        //        private static readonly string SourceDataPath = Path.Combine(
        //            Mod.DataDir,
        //            $"SourceData_v{CurrentSourceDataVersion}_debug.ADC_json"
        //        );
        //#else
        //#endif
        private static readonly string SourceDataPath = Path.Combine(
            Mod.DataDir,
            $"SourceData_v{CurrentSourceDataVersion}.ADC_json"
        );

        private static readonly string ComponentDataPath = Path.Combine(
            Mod.DataDir,
            $"ComponentData_v{CurrentComponentDataVersion}.ADC_json"
        );

        internal const int CurrentSourceDataVersion = 3;
        internal const int CurrentComponentDataVersion = 3;
        private const int MaxExtraFileLists = 25;
        private static long lastServerTime = 0;
        private DateTime nextRunTime = DateTime.MinValue;

        private static readonly HashSet<SourceDataRow> AllSources = new();

        private static ExecutableAsset? SMC = null;
        private static Dictionary<string, LoadedModInfo> LoadedCodeModsFromSMC = new();
        private static Dictionary<string, LoadedModInfo> LoadedPackagesFromSMC = new();
        private static readonly HashSet<string> FailedPackages = new();
        private static readonly HashSet<string> TriedPackages = new();
        private static bool SentFileList = false;

        private static string Username = string.Empty;
        private const string ApiBase = ApiKeyLocal.Web;

        private Stopwatch? stopwatch = null;

        protected override void OnCreate()
        {
            base.OnCreate();
            ModHelper.AddAfterActivePlaysetOrModStatusChanged(TriggerStart);
        }

        protected override void OnUpdate() { }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            AfterGameLoadingComplete();
        }

        public void AfterGameLoadingComplete()
        {
            if (TaskRunning || DisabledForSession)
                return;

            if (WorldHelper.IsMainMenu)
            {
                if (FirstMethodRan)
                    Start();
                else
                    FirstRunMethod();
                return;
            }

            if (WorldHelper.IsGameOrEditor && !DisabledForSession)
            {
                NotificationSystem.Pop(notifIdentifier);
                DisabledTemporarily = true;
                DisabledForSession = true;
                LogHelper.SendLog("Disabling all subsequent checking during this session");
                return;
            }

            if (!WorldHelper.IsGameOrEditor)
                DisabledTemporarily = false;

            if (DisabledTemporarily)
                return;

            Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
        }

        private void TriggerStart()
        {
            if (DisabledForSession)
                return;
            LogHelper.SendLog("Playset updated... Retriggering scan");
            SentFileList = false;
            if (FirstMethodRan)
                Start();
            else
                FirstRunMethod();
        }

        private static readonly HttpClientHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        private static readonly HttpClient Http = new(handler);

        private bool FirstRunMethod()
        {
            if (FirstMethodRan)
                return true;
            GameManager gmi = GameManager.instance;

            if (
                !(
                    gmi != null
                    && gmi.modManager.isInitialized
                    && gmi.gameMode == GameMode.MainMenu
                    && gmi.state == GameManager.State.WorldReady
                )
            )
            {
                TaskHelper.DelayAndAction(() =>
                    Colossal.Core.MainThreadDispatcher.RegisterUpdater(FirstRunMethod)
                );
                return false;
            }

            LogHelper.SendLog("Running FirstMethod", LogLevel.DEVD);
            Start();
            FirstMethodRan = true;
            return true;
        }

        internal static HashSet<string> PopulateLoadedPackages(
            Dictionary<string, LoadedModInfo> loadedPackages
        )
        {
            HashSet<string> packages = new();
            if (loadedPackages == null || loadedPackages.Count == 0)
                return packages;

            foreach (var item in loadedPackages)
            {
                if (
                    item.Value.AccessControl == null
                    || item.Value.AccessControl != ModAccessControlLevelState.Public
                )
                {
                    LogHelper.SendLog(
                        $"Skipping {item.Key} [{item.Value.Id}] because it is {item.Value.AccessControl}",
                        LogLevel.DEVD
                    );
                    continue;
                }

                packages.Add(item.Value.Id + "_" + item.Value.Version);
            }
            return packages;
        }

        public void Start()
        {
            nextRunTime = DateTime.MinValue;
            Cancelled = false;
            NotificationSystem.Push(
                notifIdentifier,
                title: Mod.Name,
                text: LocaleHelper.Translate($"{notifIdentifier}.Init"),
                progressState: ProgressState.Indeterminate
            );

            if (TaskRunning || DisabledForSession)
            {
                LogHelper.SendLog(
                    $"TaskRunning: {TaskRunning} or DisabledForSession {DisabledForSession}",
                    LogLevel.Debug
                );
                return;
            }

            if (!ModHelper.IsOnPublicBuild())
            {
                CancelTask(true, "Steam build is not the public build");
                return;
            }

            if (!ModHelper.IsModActive("SimpleModCheckerPlus"))
            {
                CancelTask(true, "Required mod Simple Mod Checker is missing");
                return;
            }

            if (Mod.m_Setting.AskedForConsent && !Mod.m_Setting.ContribEnabled)
            {
                CancelTask(true, "Contribution is disabled");
                return;
            }

            Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
        }

        public async void DispatchOnMain()
        {
            bool hasEAI = ModHelper.IsModActive("ExtraAssetsImporter");
#if !DEBUG
            if (!FirstLoopDone)
                LogHelper.SendLog(
                    $"EAI {(hasEAI ? "is active" : "not present")}. Waiting for {(hasEAI ? 60 : 10)} seconds."
                );

            await Task.Delay(
                Math.Clamp(
                    FirstLoopDone ? Mod.m_Setting.TaskDelay : (hasEAI ? 60 : 10),
                    1,
                    Constants.DelayMax
                ) * 1000
            );
#endif

            if (DisabledForSession)
                return;

            if (TaskRunning)
            {
                await Task.Delay(10000);
                Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
                return;
            }

            if (DateTime.Now < nextRunTime)
            {
                await Task.Delay(10000);
                Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
                return;
            }

            lastServerTime = 0;
            Cancelled = false;
            DisabledTemporarily = false;

            nextRunTime = DateTime.Now.AddMinutes(
                Math.Clamp(Mod.m_Setting.Cooldown, Constants.CooldownMin, Constants.CooldownMax)
            );
            try
            {
                #region GetConsent
                if (string.IsNullOrEmpty(Username))
                {
                    await ModHelper.CacheLoggedInUserName();
                    Username = ModHelper.UserName;

                    if (string.IsNullOrEmpty(Username))
                    {
                        CancelTask(true, "Unable to retrieve user data");
                        return;
                    }

                    if (!Mod.m_Setting.AskedForConsent)
                    {
                        LocaleHelper.AddLocalization(
                            $"{Mod.Id}.UsernameConsentText2",
                            LocaleHelper
                                .Translate($"{Mod.Id}.UsernameConsentText")
                                .Replace("{username}", Username)
                        );
                        LocaleHelper.FlushLocalizationQueue();

                        int d1 = await DialogHelper.ShowConfirmationDialogAndWait(
                            Mod.Name,
                            $"{Mod.Id}.GeneralConsentText",
                            "Paradox.CONSENT",
                            "Common.NO",
                            null
                        );
                        if (d1 == 0)
                        {
                            Mod.m_Setting.ContribEnabled = true;
                            Mod.m_Setting.ConsentForContribution = true;
                            int d2 = await DialogHelper.ShowConfirmationDialogAndWait(
                                Mod.Name,
                                $"{Mod.Id}.UsernameConsentText2",
                                "Paradox.CONSENT",
                                "Common.NO",
                                null
                            );

                            if (d2 == 0)
                                Mod.m_Setting.ConsentForUsernameShare = true;
                            else
                                Mod.m_Setting.ConsentForUsernameShare = false;
                        }
                        else
                        {
                            Mod.m_Setting.ConsentForContribution = false;
                            Mod.m_Setting.ConsentForUsernameShare = false;
                        }

                        Mod.m_Setting.AskedForConsent = true;
                    }

                    if (Mod.m_Setting.AskedForConsent && !Mod.m_Setting.ConsentForContribution)
                    {
                        CancelTask(true, "User did not consent for contribution");
                        return;
                    }
                }
                #endregion GetConsent

                if (!FindSMC())
                {
                    CancelTask(true, "SMC assembly not found? How?");
                    return;
                }

                stopwatch = Stopwatch.StartNew();
                await Task.Run(ValidateServerData);
                if (Cancelled)
                    return;
                await WorldHelper.RunOnMainThreadAsync(DoDataExtractions);
                if (Cancelled)
                    return;
                await WorldHelper.RunOnMainThreadAsync(CollectImages);
                if (Cancelled)
                    return;
                await Task.Run(ZipImages);
                if (Cancelled)
                    return;
                await Task.Run(SubmitZips);
                if (Cancelled)
                    return;
                CleanupFolders();
                if (Cancelled)
                    return;

                NotificationSystem.Pop(
                    notifIdentifier,
                    text: LocaleHelper.Translate($"{notifIdentifier}.Completed"),
                    progressState: ProgressState.Complete,
                    delay: 10
                );

                DataCacheSource newCache = new()
                {
                    ServerTime = lastServerTime,
                    CodeVersion = Mod.CodeVersion,
                    Rows = AllSources
                        .GroupBy(x => x.SourceId)
                        .Select(group => new Dictionary<string, string[]>
                        {
                            [group.Key] = group.Select(x => x.SourceVersion).Distinct().ToArray(),
                        })
                        .ToList(),
                    Version = CurrentSourceDataVersion,
                    Checksum = MD5Check(AllSources, lastServerTime.ToString()),
                };

                File.WriteAllText(SourceDataPath, JsonConvert.SerializeObject(newCache));

                LogHelper.SendLog(
                    $"Wrote latest SourceData for ServerTime {newCache.ServerTime}",
                    LogLevel.DEVD
                );

                var modsSent = ModsAdded.Except(ModsToReject).ToList();

                LogHelper.SendLog(
                    $"Successfully contributed with {modsSent.Count} mod data: {string.Join(", ", modsSent)}"
                );

                if (stopwatch.IsRunning)
                {
                    stopwatch.Stop();
                    LogHelper.SendLog($"Done in {stopwatch.Elapsed}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.SendLog($"Something went wrong: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                FirstLoopDone = true;
            }

            if (!DisabledForSession)
            {
                Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
                LogHelper.SendLog(
                    $"Queuing for next run in {StringHelper.FormatTime(nextRunTime.Second, "")}"
                );
            }

            TaskRunning = false;
        }

        public bool FindSMC()
        {
            try
            {
                SMC = ModHelper.GetModExecutable("SimpleModCheckerPlus");
                if (SMC == null)
                {
                    LogHelper.SendLog($"SMC assembly not found? How?");
                    return false;
                }

                TimeSpan timeout = TimeSpan.FromSeconds(60 * 5);
                Stopwatch stopwatch = Stopwatch.StartNew();

                Type? ModCheckupType = SMC.assembly.GetType(
                    "SimpleModCheckerPlus.Systems.ModCheckup"
                );
                if (ModCheckupType == null)
                    return false;

                FieldInfo? f1 = ModCheckupType.GetField(
                    "ModScanCompleted",
                    BindingFlags.Public | BindingFlags.Static
                );
                if (f1 == null)
                    return false;

                bool completed = (bool)f1.GetValue(null);
                while (!completed)
                {
                    LogHelper.SendLog(
                        $"ModScanCompleted is {completed} ({StringHelper.FormatTime((float)stopwatch.Elapsed.TotalSeconds)})",
                        LogLevel.DEVD
                    );
                    if (stopwatch.Elapsed >= timeout)
                    {
                        LogHelper.SendLog($"Mod scan did not complete within 5 minutes");
                        return false;
                    }
                    Task.Delay(10000).Wait();
                    completed = (bool)f1.GetValue(null);
                }

                static Dictionary<string, LoadedModInfo> ProcessMods(IDictionary mods)
                {
                    Dictionary<string, LoadedModInfo> tempDict = new();
                    foreach (DictionaryEntry entry in mods)
                    {
                        string key = (string)entry.Key;
                        object remoteObj = entry.Value;
                        if (remoteObj == null)
                            continue;

                        Type remoteType = remoteObj.GetType();
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                        LoadedModInfo localInfo = new()
                        {
                            Id = (string)remoteType.GetProperty("Id")?.GetValue(remoteObj),
                            DisplayName = (string)
                                remoteType.GetProperty("DisplayName")?.GetValue(remoteObj),
                            Author = (string)remoteType.GetProperty("Author")?.GetValue(remoteObj),
                            Version = (string)
                                remoteType.GetProperty("Version")?.GetValue(remoteObj),
                            LatestVersion = (string)
                                remoteType.GetProperty("LatestVersion")?.GetValue(remoteObj),
                            UserModVersion = (string)
                                remoteType.GetProperty("UserModVersion")?.GetValue(remoteObj),
                            Size = (ulong)(
                                remoteType.GetProperty("Size")?.GetValue(remoteObj) ?? 0UL
                            ),
                            Active = (bool)(
                                remoteType.GetProperty("Active")?.GetValue(remoteObj) ?? false
                            ),
                            AccessControl = (ModAccessControlLevelState)(
                                remoteType.GetProperty("AccessControl")?.GetValue(remoteObj)
                                ?? ModAccessControlLevelState.Unknown
                            ),
                        };
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

                        tempDict[key] = localInfo;
                    }

                    return tempDict;
                }

                FieldInfo? codesField = ModCheckupType.GetField(
                    "codes",
                    BindingFlags.Public | BindingFlags.Static
                );
                FieldInfo? packagesField = ModCheckupType.GetField(
                    "packages",
                    BindingFlags.Public | BindingFlags.Static
                );

                bool anyLoaded = false;
                try
                {
                    if (codesField?.GetValue(null) is IDictionary loadedCodes)
                    {
                        LoadedCodeModsFromSMC = ProcessMods(loadedCodes);
                        anyLoaded = true;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.SendLog(ex);
                }
                try
                {
                    if (packagesField?.GetValue(null) is IDictionary loadedPackages)
                    {
                        LoadedPackagesFromSMC = ProcessMods(loadedPackages);
                        anyLoaded = true;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.SendLog(ex);
                }

                return anyLoaded;
            }
            catch (Exception ex)
            {
                LogHelper.SendLog(ex);
                return false;
            }
        }

        public async Task ValidateServerData()
        {
            AllSources.Clear();
            int secDiff = 5 * 60; //5 mins in seconds
#if DEBUG
            secDiff = 0;
#endif

            LogHelper.SendLog("Starting ValidateServerData", LogLevel.DEVD);
            if (TaskRunning || DisabledForSession)
                return;

            TaskRunning = true;

            CleanupFolders();

            string? since = null;
            DataCacheSource? cache = null;

            if (!File.Exists(SourceDataPath))
            {
                LogHelper.SendLog("SourceData not found, initiating first run...");
            }
            else
            {
                LogHelper.SendLog("SourceData found", LogLevel.DEVD);
                try
                {
                    cache = JsonConvert.DeserializeObject<DataCacheSource>(
                        File.ReadAllText(SourceDataPath)
                    );
                    if (cache == null)
                        goto sourceDataDownload;

                    if (
                        cache.Version != CurrentSourceDataVersion
                        || cache.CodeVersion != Mod.CodeVersion
                    )
                    {
                        cache.ServerTime = 0;
                        cache.Rows = new();
                        LogHelper.SendLog("SourceData version mismatch, wiping existing data...");
                        goto sourceDataDownload;
                    }

                    string cacheChecksum = MD5Check(
                        HashSDRFromServerDict(cache.Rows),
                        cache.ServerTime.ToString()
                    );

                    if (
                        !string.Equals(
                            cacheChecksum,
                            cache.Checksum,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        LogHelper.SendLog(
                            $"SourceData checksum mismatch, discarding cache, redownloading...\n'{cache.Checksum}' expected\n'{cacheChecksum}' found"
                        );
                        cache = new();
                        goto sourceDataDownload;
                    }

                    long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cache.ServerTime;
                    string ageText = $"SourceData is {StringHelper.FormatTime(age)}.";
                    if (age < secDiff)
                    {
                        CancelTask(false, ageText);
                        return;
                    }

                    if (cache.ServerTime == 0)
                        LogHelper.SendLog("SourceData time is 0, redownloading...");
                    else
                        LogHelper.SendLog(ageText);

                    since = cache.ServerTime.ToString();
                }
                catch (Exception ex)
                {
                    LogHelper.SendLog(
                        $"Exception while reading SourceData: {ex.Message}",
                        LogLevel.Info
                    );
                    cache = null;
                }
            }

            sourceDataDownload:
            string sourceDataUrl =
                since != null
                    ? $"{ApiBase}/cs2db/source-data?since={since}"
                    : $"{ApiBase}/cs2db/source-data";

            HttpResponseMessage response;

            try
            {
                LogHelper.SendLog("Downloading latest SourceData", LogLevel.DEVD);
                using HttpRequestMessage request = new(HttpMethod.Get, sourceDataUrl);
                request.Headers.Add("X-Api-Key", ApiKeyLocal.Value);
                request.Headers.Add("X-Version", CurrentSourceDataVersion.ToString());
                response = await Http.SendAsync(request);
            }
            catch (HttpRequestException ex)
            {
                if (
                    ex.InnerException is WebException webEx
                    && webEx.Status == WebExceptionStatus.ConnectFailure
                )
                {
                    CancelTask(false, "Connection to the server failed");
                }
                else
                {
                    LogHelper.SendLog(ex, LogLevel.Info);
                    CancelTask(false, "Unknown error occured");
                }
                return;
            }
            catch (Exception ex)
            {
                CancelTask(false, ex.ToString());
                return;
            }

            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                LogHelper.SendLog(await response.Content.ReadAsStringAsync(), LogLevel.DEVD);
                CancelTask(false, $"Response failed: [{response.StatusCode}]");
                return;
            }

            ServerSourceDataResponse? parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<ServerSourceDataResponse>(body);
                LogHelper.SendLog($"Parsed SourceData", LogLevel.DEVD); //: {parsed.ToJSONString()}
            }
            catch (Exception ex)
            {
                CancelTask(false, $"Failed parsing SourceData. Try again later...");
                LogHelper.SendLog(
                    $"{ex.Message}\nEncoding: {response.Content.Headers.ContentEncoding}"
                );
                return;
            }

            if (parsed == null)
            {
                CancelTask(false, "Failed parsing SourceData. Try again later...");
                return;
            }

            if (!Game.Version.current.shortVersion.StartsWith(parsed.GameVersion))
            {
                CancelTask(
                    true,
                    "Current game version is not yet approved for contribution. Try again later..."
                );
                return;
            }

            if (parsed.CodeVersion != Mod.CodeVersion)
            {
                CancelTask(
                    true,
                    $"Mod code version ({Mod.CodeVersion}) differs from server ({parsed.CodeVersion}). Update the mod..."
                );
                return;
            }

            Dictionary<string, SourceDataRow>? merged = new();

            if (cache != null)
            {
                foreach (Dictionary<string, string[]> row in cache.Rows)
                {
                    foreach ((string SourceId, string[] Versions) in row)
                    {
                        if (string.IsNullOrEmpty(SourceId))
                            continue;

                        foreach (string sourceVersion in Versions)
                        {
                            if (string.IsNullOrEmpty(sourceVersion))
                                continue;

                            SourceDataRow sdr = new()
                            {
                                SourceId = SourceId,
                                SourceVersion = sourceVersion,
                            };

                            merged[$"{SourceId}|{sourceVersion}"] = sdr;
                        }
                    }
                }
            }

            foreach (List<object> row in parsed.SourceData)
            {
                object sourceIdObj = row[0];

                if (sourceIdObj == null || sourceIdObj.ToString() == string.Empty)
                    continue;

                List<string> versions = ((JArray)row[1]).ToObject<List<string>>() ?? new();

                foreach (string sourceVersion in versions)
                {
                    if (sourceVersion == null || sourceVersion == string.Empty)
                        continue;

                    SourceDataRow sdr = new()
                    {
                        SourceId = sourceIdObj.ToString(),
                        SourceVersion = sourceVersion,
                    };

                    merged[$"{sdr.SourceId}|{sdr.SourceVersion}"] = sdr;
                }
            }

            LogHelper.SendLog(
                $"Merged SourceData", //:\n{string.Join(", ", merged.Keys)}
                LogLevel.DEVD
            );

            lastServerTime = parsed.ServerTime;

            List<SourceDataRow> rows = merged.Values.ToList();
            AllSources.UnionWith(rows);

            DataCacheSource newCache = new()
            {
                ServerTime = lastServerTime,
                CodeVersion = Mod.CodeVersion,
                Rows = AllSources
                    .GroupBy(x => x.SourceId)
                    .Select(group => new Dictionary<string, string[]>
                    {
                        [group.Key] = group.Select(x => x.SourceVersion).Distinct().ToArray(),
                    })
                    .ToList(),
                Version = CurrentSourceDataVersion,
                Checksum = MD5Check(AllSources, lastServerTime.ToString()),
            };

            File.WriteAllText(SourceDataPath, JsonConvert.SerializeObject(newCache));

            HashSet<string> knownModKeys = rows.Select(r => $"{r.SourceId}_{r.SourceVersion ?? ""}")
                .ToHashSet();

            HashSet<string> packages = PopulateLoadedPackages(LoadedPackagesFromSMC);

            Random rng = new();
            List<string> packagesToValidate = packages
                .Except(TriedPackages)
                .Except(FailedPackages)
                .Except(knownModKeys)
                .OrderBy(_ => rng.Next())
                .ToList();

            if (packagesToValidate.Count <= 0)
            {
                CancelTask(true, "No packages found to be validated");
                return;
            }

            NotificationSystem.Push(
                notifIdentifier,
                text: LocaleHelper.Translate($"{notifIdentifier}.Verifying"),
                progressState: ProgressState.Indeterminate
            );

            int validableMax = Math.Clamp(
                Mod.m_Setting.PackCount,
                Constants.PackCountMin,
                Constants.PackCountMax
            );

            LogHelper.SendLog(
                $"Validating from {packagesToValidate.Count} packages (max {validableMax})"
            );

            foreach (string package in packagesToValidate)
                TriedPackages.Add(package);

            Dictionary<string, List<FileHelper.FileEntry>> fileLists = new();

            HashSet<string> modsToCheck = (
                await ValidateModFoldersAsync(packagesToValidate, validableMax, fileLists)
            ).ToHashSet();

            if (modsToCheck.Count <= 0)
            {
                CancelTask(false, "No verified mods found to be scanned");
                return;
            }

            LogHelper.SendLog(
                $"Preparing scan for {modsToCheck.Count} mods: {string.Join(", ", modsToCheck)}"
            );

            NotificationSystem.Push(
                notifIdentifier,
                text: LocaleHelper.Translate($"{notifIdentifier}.Starting"),
                progressState: ProgressState.Indeterminate
            );

            ModsToCheck = modsToCheck;

            foreach (string item in ModsToCheck)
            {
                string[] splits = item.Split("_") ?? Array.Empty<string>();
                if (splits.Length > 1)
                    AllSources.Add(
                        new SourceDataRow() { SourceId = splits[0], SourceVersion = splits[1] }
                    );
            }
            await GetComponentData();

            if (!SentFileList)
            {
                HashSet<string> combo = PopulateLoadedPackages(LoadedCodeModsFromSMC);
                combo.UnionWith(packages);
                await ProcessFileLists(combo, fileLists);
                SentFileList = true;
            }
        }

        public async Task ProcessFileLists(
            HashSet<string> packages,
            Dictionary<string, List<FileHelper.FileEntry>> fileLists
        )
        {
            string checkUrl = $"{ApiBase}/cs2db/file-data";
            string manifestJson = JsonConvert.SerializeObject(packages);

            using HttpRequestMessage? request = new(HttpMethod.Post, checkUrl);
            request.Content = new StringContent(manifestJson, Encoding.UTF8, "application/json");
            request.Headers.Add("X-Api-Key", ApiKeyLocal.Value);

            HttpResponseMessage? response = await Http.SendAsync(request);

            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                LogHelper.SendLog(body, LogLevel.DEVD);
                CancelTask(false, $"File list check rejected: {response.StatusCode}");
                return;
            }

            FileListResponse? parsed = JsonConvert.DeserializeObject<FileListResponse>(body);

            if (parsed is null)
            {
                CancelTask(false, "File list check returned an invalid response.");
                return;
            }

            Dictionary<string, List<FileHelper.FileEntry>> payload = new();
            List<string> missing = new();

            foreach (string package in parsed.Needed)
            {
                if (fileLists.TryGetValue(package, out List<FileHelper.FileEntry>? entries))
                    payload[package] = entries;
                else
                    missing.Add(package);
            }

            if (missing.Count > 0)
            {
                Random rng = new();
                List<string> batch = missing.OrderBy(_ => rng.Next()).ToList();
                await ValidateModFoldersAsync(batch, MaxExtraFileLists, payload);
            }

            if (payload.Count == 0)
                return;

            string storeUrl = $"{ApiBase}/cs2db/file-lists";
            string storeJson = JsonConvert.SerializeObject(new { packages = payload });

            using HttpRequestMessage upload = new(HttpMethod.Post, storeUrl);
            upload.Content = new StringContent(storeJson, Encoding.UTF8, "application/json");
            upload.Headers.Add("X-Api-Key", ApiKeyLocal.Value);

            using HttpResponseMessage uploadResponse = await Http.SendAsync(upload);

            if (!uploadResponse.IsSuccessStatusCode)
            {
                LogHelper.SendLog(await response.Content.ReadAsStringAsync(), LogLevel.DEVD);
                CancelTask(false, $"File list upload rejected: {uploadResponse.StatusCode}");
            }
            return;
        }

        public async Task GetComponentData()
        {
            string? since = null;
            DataCacheComponent? cache = null;

            if (!File.Exists(ComponentDataPath))
            {
                LogHelper.SendLog("ComponentData not found, initiating first run...");
            }
            else
            {
                LogHelper.SendLog("ComponentData found", LogLevel.DEVD);
                try
                {
                    cache = JsonConvert.DeserializeObject<DataCacheComponent>(
                        File.ReadAllText(ComponentDataPath)
                    );

                    if (cache != null)
                    {
                        string cacheChecksum = MD5Check(
                            cache.ComponentData,
                            cache.ServerTime.ToString()
                        );

                        if (
                            !string.Equals(
                                cache.Checksum,
                                cacheChecksum,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            LogHelper.SendLog(
                                $"ComponentData checksum mismatch, discarding cache and redownloading...\n'{cache.Checksum}' expected\n'{cacheChecksum}' found"
                            );
                            cache = null;
                        }
                    }

                    if (cache != null && cache.Version != CurrentComponentDataVersion)
                    {
                        cache.ServerTime = 0;
                        cache.ComponentData = new();
                        LogHelper.SendLog("ComponentData version changed, wiping existing data...");
                    }

                    if (cache != null)
                    {
                        long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cache.ServerTime;
                        string ageText = $"ComponentData is {StringHelper.FormatTime(age)}.";

                        if (cache.ServerTime == 0)
                            LogHelper.SendLog("ComponentData time is 0, redownloading...");
                        else
                            LogHelper.SendLog(ageText);

                        since = cache.ServerTime.ToString();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.SendLog(
                        $"Exception while reading ComponentData: {ex.Message}",
                        LogLevel.Info
                    );
                    cache = null;
                }
            }

            string url =
                since != null
                    ? $"{ApiBase}/cs2db/component-data?since={since}"
                    : $"{ApiBase}/cs2db/component-data";

            HttpResponseMessage response;

            try
            {
                LogHelper.SendLog("Downloading latest ComponentData", LogLevel.DEVD);
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                request.Headers.Add("X-Api-Key", ApiKeyLocal.Value);
                request.Headers.Add("X-Version", CurrentComponentDataVersion.ToString());
                response = await Http.SendAsync(request);
            }
            catch (HttpRequestException ex)
            {
                if (
                    ex.InnerException is WebException webEx
                    && webEx.Status == WebExceptionStatus.ConnectFailure
                )
                {
                    if (cache != null)
                    {
                        ApplyCacheToLib(cache);
                        return;
                    }
                    CancelTask(false, "Connection to the server failed");
                }
                else
                {
                    LogHelper.SendLog(ex, LogLevel.Info);
                    CancelTask(false, "Unknown error occured");
                }
                return;
            }
            catch (Exception ex)
            {
                CancelTask(false, ex.ToString());
                return;
            }

            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (cache != null)
                {
                    ApplyCacheToLib(cache);
                    return;
                }
                LogHelper.SendLog(body, LogLevel.DEVD);
                CancelTask(false, $"ComponentData Response failed: {response.StatusCode}");
                return;
            }

            ServerComponentDataResponse? parsed;
            try
            {
                LogHelper.SendLog($"Parsing latest ComponentData: {body}", LogLevel.DEVD);
                parsed = JsonConvert.DeserializeObject<ServerComponentDataResponse>(body);
                LogHelper.SendLog($"Parsed ComponentData", LogLevel.DEVD); //: {parsed.ToJSONString()}
            }
            catch (Exception ex)
            {
                CancelTask(false, ex.ToString());
                return;
            }

            if (parsed == null)
            {
                if (cache != null)
                    ApplyCacheToLib(cache);
                return;
            }

            Dictionary<string, ComponentAttributeRow> merged = new();

            if (cache != null)
                foreach (ComponentAttributeRow row in ListCARFromServerDict(cache.ComponentData))
                    merged[$"{row.Source}|{row.CompName}|{row.AttrName}"] = row;

            foreach (ComponentAttributeRow row in ListCARFromServerDict(parsed.ComponentData))
                merged[$"{row.Source}|{row.CompName}|{row.AttrName}"] = row;

            Dictionary<string, Dictionary<string, List<ComponentAttributeRowCompact>>> mergedRows =
                ServerDictFromListCAR(merged.Values.ToList());

            DataCacheComponent newCache = new()
            {
                Version = CurrentComponentDataVersion,
                ServerTime = parsed.ServerTime,
                ComponentData = mergedRows,
                Checksum = MD5Check(mergedRows, parsed.ServerTime.ToString()),
            };

            try
            {
                File.WriteAllText(ComponentDataPath, JsonConvert.SerializeObject(newCache));
            }
            catch (Exception ex)
            {
                LogHelper.SendLog(
                    $"Failed to write ComponentData cache: {ex.Message}",
                    LogLevel.Info
                );
            }

            ApplyCacheToLib(newCache);
        }

        private void ApplyCacheToLib(DataCacheComponent cache)
        {
            BaseComponent.Clear();
            HashSet<(int, string, string, string, string, string)> lib = new();

            List<ComponentAttributeRow> rows = ListCARFromServerDict(cache.ComponentData);

            foreach (var row in rows)
                lib.Add(
                    (
                        row.Index,
                        row.CompName ?? "",
                        row.AttrName ?? "",
                        row.AttrType ?? "",
                        row.Note ?? "",
                        row.Source ?? "Game"
                    )
                );

            BaseComponent = lib;
        }

        public void DoDataExtractions()
        {
            try
            {
                HashSet<string> tempReject = ModsToReject;
                if (ModsToReject.Count > 0)
                {
                    LogHelper.SendLog(
                        $"{ModsToReject.Count} mods were rejected for extraction: {string.Join(",", ModsToReject)}"
                    );
                }
                WorldHelper.GetSystem<ExtractionSystem>().ExtractData();

                if (ModsToReject.Count > tempReject.Count)
                {
                    LogHelper.SendLog(
                        $"{ModsToReject.Count - tempReject.Count} mods were further rejected for extraction: {string.Join(",", ModsToReject.Except(tempReject))}"
                    );
                }
            }
            catch (Exception ex)
            {
                CancelTask(false, ex.ToString());
                return;
            }
        }

        public async Task SubmitZips()
        {
            LogHelper.SendLog("Starting SubmitModZips", LogLevel.DEVD);
            if (!Directory.Exists($"{Mod.DataDir}\\~Extracted"))
            {
                CancelTask(false, "Extracted folder doesn't exist", LogLevel.DEVD);
                return;
            }

            string[] newZips = Directory.GetFiles($"{Mod.DataDir}\\~Extracted");
            if (newZips.Length <= 0)
            {
                CancelTask(false, "No zips present");
                return;
            }

            if (Mod.m_Setting.ConsentForUsernameShare && (ModsAdded.Count > 0))
            {
                var payload = new
                {
                    userName = Username,
                    userId = ModHelper.UserId.Replace("-", ""),
                    modIds = ModsAdded,
                };

                using HttpRequestMessage? request = new(
                    HttpMethod.Post,
                    $"{ApiBase}/cs2db/contribution"
                );
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json"
                );
                request.Headers.Add("X-Api-Key", ApiKeyLocal.Value);

                using HttpResponseMessage? uploadResponse = await Http.SendAsync(request);

                if (uploadResponse != null)
                {
                    switch (uploadResponse.StatusCode)
                    {
                        case HttpStatusCode.BadRequest:
                            CancelTask(false, "Bad request sent");
                            return;
                        case HttpStatusCode.Unauthorized:
                            CancelTask(false, "Unauthorized request sent");
                            return;
                        default:
                            break;
                    }
                    LogHelper.SendLog(
                        $"Upload Response: {uploadResponse.StatusCode}",
                        uploadResponse.IsSuccessStatusCode ? LogLevel.DEVD : LogLevel.Info
                    );
                }
            }

            int count = 0;
            DateTime lastTime = DateTime.MinValue;

            foreach (string zipPath in newZips)
            {
                count++;
                try
                {
                    NotificationSystem.Push(
                        notifIdentifier,
                        text: LocaleHelper.Translate($"{notifIdentifier}.Sharing"),
                        progressState: ProgressState.Progressing,
                        progress: 100 * (count / newZips.Length)
                    );

                    LogHelper.SendLog($"Sending {Path.GetFileName(zipPath)}", LogLevel.DEVD);
                    MultipartFormDataContent? content = new();
                    byte[]? bytes = await File.ReadAllBytesAsync(zipPath);
                    ByteArrayContent? fileContent = new(bytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                    content.Add(fileContent, "zip", Path.GetFileName(zipPath));

                    using HttpRequestMessage? request = new(
                        HttpMethod.Post,
                        $"{ApiBase}/cs2db/submit"
                    );
                    request.Content = content;
                    request.Headers.Add("X-Api-Key", ApiKeyLocal.Value);

                    if (lastTime != DateTime.MinValue)
                    {
                        TimeSpan elapsed = DateTime.UtcNow - lastTime;

                        if (elapsed < TimeSpan.FromSeconds(1))
                            await Task.Delay(1000);
                    }

                    lastTime = DateTime.UtcNow;

                    using HttpResponseMessage? uploadResponse = await Http.SendAsync(request);

                    if (uploadResponse != null)
                    {
                        switch (uploadResponse.StatusCode)
                        {
                            case HttpStatusCode.BadRequest:
                                CancelTask(false, "Bad request sent");
                                return;
                            case HttpStatusCode.Unauthorized:
                                CancelTask(false, "Unauthorized request sent");
                                return;
                            default:
                                break;
                        }

                        if ((int)uploadResponse.StatusCode == 422)
                        {
                            CancelTask(true, "Server rejected the content");
                            return;
                        }

                        LogHelper.SendLog(
                            $"Upload Response: {uploadResponse.StatusCode}",
                            uploadResponse.IsSuccessStatusCode ? LogLevel.DEVD : LogLevel.Info
                        );
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.SendLog(ex, LogLevel.Info);
                }
            }
        }

        private readonly List<CollectedImage> CollectedImages = new();

        public void CollectImages()
        {
            if (ImagesNeeded.Count <= 0)
            {
                LogHelper.SendLog($"No images to collect", LogLevel.DEVD);
                return;
            }

            CollectedImages.Clear();

            string tempDir = $"{Mod.DataDir}\\~ImagesTemp";
            Directory.CreateDirectory(tempDir);

            LogHelper.SendLog(
                $"Collecting {ImagesNeeded.Count} icons for submission...\n- {string.Join("\n- ", ImagesNeeded)}",
                LogLevel.DEVD
            );

            foreach (string item in ImagesNeeded)
            {
                if (!AssetHelper.TryGetAsset(item, out AssetData? ass) || ass == null)
                    continue;

                string guid = ass.id.guid.ToString();
                try
                {
                    bool isTexture = false;
                    string extension = Path.GetExtension(ass.id.uri);
                    byte[] data = Array.Empty<byte>();
                    if (ass is TextureAsset ta)
                    {
                        (bool extracted, ImageAsset.FileFormat ff_temp) =
                            TextureHelper.TryExtractImageFromTextureAsset(ta, out data);

                        if (extracted)
                        {
                            extension = ff_temp.ToExtension();
                            isTexture = true;
                        }
                    }

                    string filename = $"{guid}{extension}";
                    string destPath = Path.Combine(tempDir, filename);

                    if (isTexture)
                    {
                        if (data != null)
                        {
                            File.WriteAllBytes(destPath, data);
                        }
                        else
                        {
                            LogHelper.SendLog(
                                new Exception(
                                    $"Something went wrong...\nThe texture bytes for the image {ass.name} is null."
                                ),
                                LogLevel.Error
                            );
                            continue;
                        }
                    }
                    else
                    {
                        using Stream readStream = ass.database.GetReadStream(ass.id);
                        using FileStream fileStream = File.Create(destPath);
                        readStream.CopyTo(fileStream);
                    }

                    using SHA256 sha256 = SHA256.Create();

                    using FileStream readBack = File.OpenRead(destPath);
                    byte[] hashBytes = sha256.ComputeHash(readBack);

                    string hash = ChecksumHelper.Sha256FromBytes(hashBytes);
                    long size = new FileInfo(destPath).Length;

                    CollectedImages.Add(
                        new CollectedImage
                        {
                            GUID = guid,
                            Extension = extension,
                            Hash = hash,
                            Size = size,
                            FilePath = destPath,
                        }
                    );
                }
                catch (Exception e)
                {
                    LogHelper.SendLog(
                        $"Failed to collect image for guid '{guid}': {e.Message}",
                        LogLevel.DEVD
                    );
                }
            }
            LogHelper.SendLog($"Collected {CollectedImages.Count} images in memory", LogLevel.DEVD);
        }

        public async Task ZipImages()
        {
            if (CollectedImages.Count <= 0)
            {
                LogHelper.SendLog("No images collected", LogLevel.DEVD);
                return;
            }

            List<ImageManifestEntry> manifest = CollectedImages
                .Select(img => new ImageManifestEntry
                {
                    GUID = img.GUID,
                    Extension = img.Extension,
                    Hash = img.Hash,
                    Size = img.Size,
                })
                .ToList();

            List<string> neededGuids;
            try
            {
                string manifestJson = JsonConvert.SerializeObject(manifest);

                using HttpRequestMessage? request = new(
                    HttpMethod.Post,
                    $"{ApiBase}/cs2db/r2-manifest"
                );
                request.Content = new StringContent(
                    manifestJson,
                    Encoding.UTF8,
                    "application/json"
                );
                request.Headers.Add("X-Api-Key", ApiKeyLocal.Value);

                HttpResponseMessage? response = await Http.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    LogHelper.SendLog(await response.Content.ReadAsStringAsync(), LogLevel.DEVD);
                    CancelTask(false, $"Image manifest check rejected: {response.StatusCode}");
                    return;
                }

                string body = await response.Content.ReadAsStringAsync();
                ImageManifestResponse? parsed =
                    JsonConvert.DeserializeObject<ImageManifestResponse>(body);
                neededGuids = parsed?.Needed ?? new List<string>();
            }
            catch (Exception e)
            {
                CancelTask(false, $"Image manifest check failed: {e.Message}");
                return;
            }

            if (neededGuids.Count == 0)
            {
                LogHelper.SendLog("Server has no new/changed images to receive.", LogLevel.DEVD);
                return;
            }

            HashSet<string> neededSet = neededGuids.ToHashSet();
            List<CollectedImage> toSend = CollectedImages
                .Where(img => neededSet.Contains(img.GUID))
                .ToList();

            const long MaxZipSize = 25L * 1024 * 1024;
            List<CollectedImage> sortedImages = toSend.OrderBy(i => i.Size).ToList();

            List<List<CollectedImage>> batches = new();
            List<CollectedImage> currentBatch = new();
            long currentSize = 0;

            foreach (CollectedImage image in sortedImages)
            {
                if (currentBatch.Count == 0)
                {
                    currentBatch.Add(image);
                    currentSize = image.Size;
                    continue;
                }

                if (currentSize + image.Size <= MaxZipSize)
                {
                    currentBatch.Add(image);
                    currentSize += image.Size;
                }
                else
                {
                    batches.Add(currentBatch);

                    currentBatch = new List<CollectedImage> { image };
                    currentSize = image.Size;
                }
            }

            if (currentBatch.Count > 0)
                batches.Add(currentBatch);

            int zipIndex = 1;

            string baseDirectory = $"{Mod.DataDir}\\~Extracted";
            Directory.CreateDirectory(baseDirectory);

            string timeNow = $"{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}";

            foreach (List<CollectedImage> batch in batches)
            {
                string zipName = $"~{timeNow}_images_{zipIndex}.adc";
                string zipPath = Path.Combine(baseDirectory, zipName);

                List<ImageManifestEntry> zipManifest = batch
                    .Select(img => new ImageManifestEntry
                    {
                        GUID = img.GUID,
                        Extension = img.Extension,
                        Hash = img.Hash,
                        Size = img.Size,
                    })
                    .ToList();

                try
                {
                    using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);

                    ZipArchiveEntry manifestEntry = archive.CreateEntry(
                        "manifest.json",
                        CompressionLevel.Optimal
                    );

                    using (StreamWriter writer = new(manifestEntry.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write(JsonConvert.SerializeObject(zipManifest));
                    }

                    foreach (CollectedImage img in batch)
                    {
                        archive.CreateEntryFromFile(
                            img.FilePath,
                            $"{img.GUID}{img.Extension}",
                            CompressionLevel.NoCompression
                        );
                    }

                    LogHelper.SendLog($"Wrote {toSend.Count} images to '{zipName}'", LogLevel.DEVD);
                }
                catch (Exception e)
                {
                    CancelTask(false, $"Failed to write image zip: {e.Message}");
                    return;
                }
                zipIndex++;
            }
        }

        internal static void CleanupFolders()
        {
            LogHelper.SendLog("Cleaning up existing zips if present", LogLevel.DEVD);
            string[] folders = new[]
            {
                $"{Mod.DataDir}\\~Extracted",
                $"{Mod.DataDir}\\~ImagesTemp",
            };

            foreach (string folderPath in folders)
            {
                if (!Directory.Exists(folderPath))
                    continue;

                foreach (string file in Directory.GetFiles(folderPath))
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception e)
                    {
                        LogHelper.SendLog(e);
                    }

                foreach (string dir in Directory.GetDirectories(folderPath))
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch (Exception e)
                    {
                        LogHelper.SendLog(e);
                    }
            }
        }

        internal void CancelTask(
            bool disabled = false,
            string? log = null,
            LogLevel logLevel = LogLevel.Info
        )
        {
            FirstLoopDone = true;
            Cancelled = true;
            if (NotificationSystem.Exist(notifIdentifier))
                NotificationSystem.Pop(
                    notifIdentifier,
                    delay: 3,
                    title: Mod.Name,
                    text: LocaleHelper.Translate($"{notifIdentifier}.Cancelled"),
                    progressState: ProgressState.Cancelled,
                    onClicked: () =>
                    {
                        WorldHelper
                            .GetSystem<OptionsUISystem>()
                            .OpenPage($"{Mod.Id}.{Mod.Id}.Mod", "Setting.LogTab", false);
                    }
                );

            if (log != null)
                LogHelper.SendLog(
                    $"{log.TrimEnd('.')}. {(disabled ? "Disabling for the session" : "Cancelling temporarily")}...",
                    logLevel
                );

            DisabledForSession = disabled;
            TaskRunning = false;
            if (stopwatch != null && stopwatch.IsRunning)
            {
                stopwatch.Stop();
                LogHelper.SendLog($"Cancelled after {stopwatch.Elapsed}");
            }

            if (!DisabledForSession)
                Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
        }

        internal static async Task<HashSet<string>> ValidateModFoldersAsync(
            List<string> packages,
            int validableMax,
            Dictionary<string, List<FileHelper.FileEntry>> fileLists
        )
        {
            string[]? roots = ModHelper.GetPDXModsPath().Where(Directory.Exists).ToArray();

            HashSet<string> validPackages = new();

            foreach (string root in roots)
            {
                foreach (string modFolder in packages)
                {
                    string subfolder = Path.Combine(root, modFolder);

                    if (!Directory.Exists(subfolder))
                        continue;

                    string metadataFile = Path.Combine(subfolder, ".metadata", "metadata.json");

                    string[] modFolderParts = modFolder.Split('_');
                    if (modFolderParts.Length != 2)
                        continue;

                    ModData modData = new()
                    {
                        ModId = modFolderParts[0],
                        ModName = modFolderParts[0],
                        ModVersion = modFolderParts[1],
                    };

                    try
                    {
                        if (File.Exists(metadataFile))
                        {
                            JObject jsonObject = JObject.Parse(
                                await File.ReadAllTextAsync(metadataFile)
                            );
                            modData.ModName =
                                jsonObject["DisplayName"]?.ToString()
                                ?? jsonObject["displayName"]?.ToString()
                                ?? modData.ModId.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.SendLog("Error reading metadata: " + ex.Message);
                    }

                    string? manifestPath = ManifestHelper.FindManifestFile(subfolder);
                    if (string.IsNullOrEmpty(manifestPath))
                    {
                        LogHelper.SendLog($"Error finding manifest file at '{manifestPath}'");
                        continue;
                    }

                    Dictionary<string, string> manifestData;

                    try
                    {
                        manifestData = ManifestHelper.ReadManifestFile(manifestPath);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.SendLog(
                            $"Error reading manifest at '{manifestPath}': {ex.Message}"
                        );
                        continue;
                    }

                    if (manifestData.Count <= 0)
                    {
                        LogHelper.SendLog($"Manifest file returned empty at '{manifestPath}'");
                        continue;
                    }

                    bool valid = false;
                    try
                    {
                        valid = TryVerifyFolderFiles(subfolder, manifestData);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.SendLog($"Error verifying files in '{subfolder}': {ex.Message}");
                    }

                    if (valid)
                    {
                        LogHelper.SendLog($"{modFolder} is valid", LogLevel.DEVD);
                        validPackages.Add(modFolder);

                        fileLists[modFolder] = FileHelper.ListFiles(subfolder, true);

                        if (validPackages.Count >= validableMax)
                        {
                            return validPackages;
                        }
                        continue;
                    }
                    FailedPackages.Add(modFolder);
                    LogHelper.SendLog($"{modFolder} failed to be verified");
                }
            }
            return validPackages;
        }

        public static bool TryVerifyFolderFiles(
            string subfolder,
            Dictionary<string, string> manifestData
        )
        {
            if (!Directory.Exists(subfolder))
            {
                LogHelper.SendLog($"Subfolder {subfolder} doesn't exist", LogLevel.DEVD);
                return false;
            }

            var files = Directory
                .EnumerateFiles(subfolder, "*", SearchOption.AllDirectories)
                .Where(file => !file.Contains(".metadata") && !file.Contains(".cpatch"))
                .ToArray();

            foreach (string filePath in files)
            {
                string relativePath = FileHelper
                    .GetRelativePath(subfolder, filePath)
                    .Replace("/", "\\");
                string relativePathForText = $"{relativePath.Replace("\\", "/")}";
                try
                {
                    if (manifestData.TryGetValue(relativePath, out var entry))
                    {
                        string[] manifestParts = entry.Split(
                            new string[] { ";;" },
                            StringSplitOptions.None
                        );
                        string expectedSize = manifestParts[0];
                        string expectedHash = manifestParts[1];

                        long actualSize = new FileInfo(filePath).Length;

                        if (
                            !string.Equals(
                                expectedSize,
                                actualSize.ToString(),
                                StringComparison.Ordinal
                            )
                        )
                        {
                            LogHelper.SendLog(
                                $"Size mismatch in {subfolder}/{relativePathForText} (expected: {expectedSize}, actual: {actualSize})",
                                LogLevel.DEVD
                            );
                            return false;
                        }

                        string? actualHash = ChecksumHelper.Sha256FromFile(filePath);

                        if (
                            actualHash != null
                            && !string.Equals(expectedHash, actualHash, StringComparison.Ordinal)
                        )
                        {
                            LogHelper.SendLog(
                                $"Hash mismatch in {subfolder}/{relativePathForText} (expected: {expectedHash}, actual: {actualHash})",
                                LogLevel.DEVD
                            );
                            return false;
                        }

                        manifestData.Remove(relativePath);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.SendLog(
                        $"Exception when verifying {subfolder}/{relativePathForText}: {ex.Message}",
                        LogLevel.DEVD
                    );
                    return false;
                }
            }

            if (manifestData.Count > 0)
            {
                LogHelper.SendLog($"ManifestData is empty", LogLevel.DEVD);
                return false;
            }

            return true;
        }

        public static void CleanupLocalData()
        {
            CleanupFolders();

            foreach (string file in Directory.EnumerateFiles(Mod.DataDir))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    LogHelper.SendLog($"Failed to delete outdated SourceData file '{file}': {ex}");
                }
            }

            LogHelper.SendLog("Cleanup successful");
        }

        public string MD5Check(HashSet<SourceDataRow> sdr, string lst)
        {
            string checksumInput =
                string.Join(
                    "",
                    sdr.OrderBy(x => x.SourceId, StringComparer.Ordinal)
                        .ThenBy(x => x.SourceVersion, StringComparer.Ordinal)
                        .Select(x => $"{x.SourceId}\t{x.SourceVersion}")
                ) + $"|{lst}|{CurrentSourceDataVersion}";
            //LogHelper.SendLog($"{checksumInput} for SDR", LogLevel.DEVD);

            string checksum = ChecksumHelper.CreateMD5(checksumInput);

            return checksum;
        }

        public string MD5Check(
            Dictionary<string, Dictionary<string, List<ComponentAttributeRowCompact>>> car,
            string lst
        )
        {
            string checksumInput =
                string.Join(
                    "",
                    car.SelectMany(source =>
                        source.Value.SelectMany(comp =>
                            comp.Value.Select(r =>
                                $"{r.Index}|{comp.Key}|{r.AttrName}|{r.AttrType}|{r.Note}|{source.Key}"
                            )
                        )
                    )
                ) + $"|{lst}|{CurrentComponentDataVersion}";
            //LogHelper.SendLog($"{checksumInput} for CARC", LogLevel.DEVD);

            string checksum = ChecksumHelper.CreateMD5(checksumInput);

            return checksum;
        }

        public List<ComponentAttributeRow> ListCARFromServerDict(
            Dictionary<string, Dictionary<string, List<ComponentAttributeRowCompact>>> carc
        )
        {
            return carc.SelectMany(source =>
                    source.Value.SelectMany(component =>
                        component.Value.Select(attribute => new ComponentAttributeRow
                        {
                            Source = source.Key,
                            CompName = component.Key,
                            Index = attribute.Index,
                            AttrName = attribute.AttrName,
                            AttrType = attribute.AttrType,
                            Note = attribute.Note,
                        })
                    )
                )
                .ToList();
        }

        public Dictionary<
            string,
            Dictionary<string, List<ComponentAttributeRowCompact>>
        > ServerDictFromListCAR(List<ComponentAttributeRow> car)
        {
            return car.GroupBy(x => x.Source)
                .ToDictionary(
                    source => source.Key,
                    source =>
                        source
                            .GroupBy(x => x.CompName)
                            .ToDictionary(
                                component => component.Key,
                                component =>
                                    component
                                        .Select(y => new ComponentAttributeRowCompact
                                        {
                                            Index = y.Index,
                                            AttrName = y.AttrName,
                                            AttrType = y.AttrType,
                                            Note = y.Note,
                                        })
                                        .ToList()
                            )
                );
        }

        public HashSet<SourceDataRow> HashSDRFromServerDict(
            List<Dictionary<string, string[]>> cache
        )
        {
            return cache
                .SelectMany(row => row)
                .SelectMany(item =>
                    item.Value.Select(version => new SourceDataRow
                    {
                        SourceId = item.Key,
                        SourceVersion = version,
                    })
                )
                .ToHashSet();
        }
    }
}
