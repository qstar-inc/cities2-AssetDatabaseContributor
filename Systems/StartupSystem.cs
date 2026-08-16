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
using Colossal.IO.AssetDatabase;
using Colossal.Json;
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
        private static bool TaskRunning = false;
        private static bool DisabledTemporarily = false;
        private static bool DisabledForSession = false;
        private static bool Cancelled = false;

#if DEBUG
        private static readonly string SourceDataPath = $"{Mod.DataDir}\\SourceData_debug.json";
#else
        private static readonly string SourceDataPath = $"{Mod.DataDir}\\SourceData.json";
#endif

        private static long lastServerTime = 0;
        private DateTime nextRunTime = DateTime.MinValue;

        private static readonly HashSet<SourceDataRow> AllSources = new();

        private static ExecutableAsset? SMC = null;
        private static Dictionary<string, LoadedModInfo> LoadedPackagesFromSMC = new();

        private static string Username = string.Empty;
        private const string ApiBase = ApiKeyLocal.Web;

        private Stopwatch? stopwatch = null;

        protected override void OnCreate()
        {
            base.OnCreate();
            Colossal.Core.MainThreadDispatcher.RegisterUpdater(FirstRunMethod);
        }

        protected override void OnUpdate() { }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (!FirstMethodRan || TaskRunning || DisabledForSession)
                return;

            if (WorldHelper.IsGameOrEditor && !DisabledForSession)
            {
                NotificationSystem.Pop(notifIdentifier);
                DisabledTemporarily = true;
                LogHelper.SendLog("Disabling checking while game/editor is active");
                return;
            }

            if (!WorldHelper.IsGameOrEditor)
            {
                DisabledTemporarily = false;
            }

            if (DisabledTemporarily)
                return;

            Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
        }

        private static readonly HttpClient Http = new();

        private bool FirstRunMethod()
        {
            if (FirstMethodRan)
                return true;

            if (
                !GameManager.instance.modManager.isInitialized
                || GameManager.instance.gameMode != GameMode.MainMenu
                || GameManager.instance.state == GameManager.State.Loading
                || GameManager.instance.state == GameManager.State.Booting
            )
                return false;
            FirstMethodRan = true;
            Start();
            return true;
        }

        internal static HashSet<string> PopulateLoadedPackages()
        {
            HashSet<string> packages = new();
            var loadedPackages = LoadedPackagesFromSMC;
            if (loadedPackages == null || loadedPackages.Count == 0)
                return packages;

#if DEBUG
            StringBuilder sb = new();
            if (loadedPackages.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Currently loaded asset packs:");
            }
#endif

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
#if DEBUG
                sb.AppendLine(item.Key + ": " + item.Value.Id + "_" + item.Value.Version);
            }
            LogHelper.SendLog(sb, LogLevel.DEVD);
#else
            }
#endif
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
                return;

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
            if (DisabledForSession)
                return;

            if (TaskRunning || DisabledTemporarily)
            {
                Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
                return;
            }

            if (DateTime.Now < nextRunTime)
            {
                Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
                return;
            }

            lastServerTime = 0;
            Cancelled = false;

            try
            {
                await Task.Delay(
                    Math.Clamp(Mod.m_Setting.TaskDelay, Constants.DelayMin, Constants.DelayMax)
                        * 1000
                );
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

                if (!FindSMC())
                {
                    CancelTask(true, "SMC assembly not found? How?");
                    return;
                }

                if (Mod.m_Setting.AskedForConsent && !Mod.m_Setting.ConsentForContribution)
                {
                    CancelTask(true, "User did not consent for contribution");
                    return;
                }

                stopwatch = Stopwatch.StartNew();
                await Task.Run(GetExtractData);
                if (Cancelled)
                    return;
                await WorldHelper.RunOnMainThreadAsync(DoExtractions);
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

                SourceDataCache newCache = new()
                {
                    ServerTime = lastServerTime,
                    Rows = AllSources.ToList(),
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

            nextRunTime = DateTime.Now.AddMinutes(
                Math.Clamp(Mod.m_Setting.Cooldown, Constants.CooldownMin, Constants.CooldownMax)
            );

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

                Type? helperType = SMC.assembly.GetType("SimpleModCheckerPlus.Systems.ModCheckup");
                if (helperType == null)
                    return false;

                FieldInfo? f1 = helperType.GetField(
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

                FieldInfo? f2 = helperType.GetField(
                    "packages",
                    BindingFlags.Public | BindingFlags.Static
                );
                if (f2 == null)
                    return false;

                IDictionary loadedPackages = (IDictionary)f2.GetValue(null);

                if (loadedPackages == null)
                    return false;

                Dictionary<string, LoadedModInfo> tempDict = new();
                foreach (DictionaryEntry entry in loadedPackages)
                {
                    string key = (string)entry.Key;
                    object remoteObj = entry.Value;
                    if (remoteObj == null)
                        continue;

                    Type remoteType = remoteObj.GetType();
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8601 // Possible null reference assignment.
                    LoadedModInfo localInfo = new()
                    {
                        Id = (string)remoteType.GetProperty("Id")?.GetValue(remoteObj),
                        DisplayName = (string)
                            remoteType.GetProperty("DisplayName")?.GetValue(remoteObj),
                        Author = (string)remoteType.GetProperty("Author")?.GetValue(remoteObj),
                        Version = (string)remoteType.GetProperty("Version")?.GetValue(remoteObj),
                        LatestVersion = (string)
                            remoteType.GetProperty("LatestVersion")?.GetValue(remoteObj),
                        UserModVersion = (string)
                            remoteType.GetProperty("UserModVersion")?.GetValue(remoteObj),
                        Size = (ulong)(remoteType.GetProperty("Size")?.GetValue(remoteObj) ?? 0UL),
                        Active = (bool)(
                            remoteType.GetProperty("Active")?.GetValue(remoteObj) ?? false
                        ),
                        AccessControl = (ModAccessControlLevelState)(
                            remoteType.GetProperty("AccessControl")?.GetValue(remoteObj)
                            ?? ModAccessControlLevelState.Unknown
                        ),
                    };
#pragma warning restore CS8601 // Possible null reference assignment.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

                    tempDict[key] = localInfo;
                }

                LoadedPackagesFromSMC = tempDict;

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.SendLog(ex, LogLevel.Error);
                return false;
            }
        }

        public async Task GetExtractData()
        {
            AllSources.Clear();
            int secDiff = 5 * 60; //5 mins in seconds
#if DEBUG
            secDiff = 0;
#endif

            LogHelper.SendLog("Starting GetExtractData", LogLevel.DEVD);
            if (TaskRunning || DisabledForSession)
                return;

            TaskRunning = true;

            CleanupFolders();

            string? since = null;
            SourceDataCache? cache = null;

            if (!File.Exists(SourceDataPath))
            {
                LogHelper.SendLog("SourceData not found, initiating first run...");
            }
            else
            {
                LogHelper.SendLog("SourceData found", LogLevel.DEVD);
                try
                {
                    cache = JsonConvert.DeserializeObject<SourceDataCache>(
                        File.ReadAllText(SourceDataPath)
                    );
                    if (cache != null)
                    {
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

            string url =
                since != null
                    ? $"{ApiBase}/cs2db/source-data?since={since}"
                    : $"{ApiBase}/cs2db/source-data";

            HttpResponseMessage response;

            try
            {
                LogHelper.SendLog("Downloading latest SourceData", LogLevel.DEVD);
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                request.Headers.Add("X-Api-Key", ApiKeyLocal.Value);
                request.Headers.Add("X-Version", "2");
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

            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                CancelTask(false, $"Response failed: {body}");
                return;
            }

            ServerSourceDataResponse? parsed;
            try
            {
                LogHelper.SendLog($"Parsing latest SourceData: {body}", LogLevel.DEVD);
                parsed = JsonConvert.DeserializeObject<ServerSourceDataResponse>(body);
                LogHelper.SendLog($"Parsed SourceData: {parsed.ToJSONString()}", LogLevel.DEVD);
            }
            catch (Exception ex)
            {
                CancelTask(false, ex.ToString());
                return;
            }

            Dictionary<string, SourceDataRow>? merged = new();

            if (cache != null)
            {
                foreach (SourceDataRow row in cache.Rows)
                    merged[$"{row.Source}|{row.SourceId}|{row.SourceVersion}"] = row;
            }

            foreach (SourceDataRow row in parsed.SourceData)
                merged[$"{row.Source}|{row.SourceId}|{row.SourceVersion}"] = row;

            LogHelper.SendLog(
                $"Merged SourceData:\n{string.Join(", ", merged.Keys)}",
                LogLevel.DEVD
            );

            lastServerTime = parsed.ServerTime;

            List<SourceDataRow> rows = merged.Values.ToList();
            AllSources.UnionWith(rows);

            HashSet<string> knownModKeys = rows.Where(r => r.Source == "Mod")
                .Select(r => $"{r.SourceId}_{r.SourceVersion ?? ""}")
                .ToHashSet();

            HashSet<string> packages = PopulateLoadedPackages();

            Random rng = new();
            List<string> packagesToValidate = packages
                .Except(knownModKeys)
                .OrderBy(_ => rng.Next())
                .ToList();

            if (packagesToValidate.Count <= 0)
            {
                CancelTask(false, "No packages found to be validated");
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
                $"Validating max {validableMax} out of {packagesToValidate.Count} packages"
            );

            HashSet<string> modsToCheck = (
                await ValidateModFoldersAsync(packagesToValidate, validableMax)
            ).ToHashSet();

            if (modsToCheck.Count <= 0)
            {
                CancelTask(false, "No verified mods found to be scanned");
                return;
            }

            LogHelper.SendLog(
                $"Starting scan for {modsToCheck.Count} mods: {string.Join(", ", modsToCheck)}"
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
                        new SourceDataRow()
                        {
                            Source = "Mod",
                            SourceId = splits[0],
                            SourceVersion = splits[1],
                        }
                    );
            }
        }

        public void DoExtractions()
        {
            try
            {
                if (ModsToReject.Count > 0)
                {
                    LogHelper.SendLog(
                        $"{ModsToReject.Count} mods were rejected for extraction: {string.Join(",", ModsToReject)}"
                    );
                }

                if (ModsToCheck.Count <= 0)
                {
                    CancelTask(false, "No mods selected to check");
                    return;
                }
                WorldHelper.GetSystem<ExtractionSystem>().ExtractPrefabs(Limits.Mod);
            }
            catch (Exception ex)
            {
                CancelTask(false, ex.ToString());
                return;
            }
        }

        public async Task SubmitZips()
        {
            if (ModsToReject.Count > 0)
            {
                var payload = new { rejects = ModsToReject.ToList() };

                using HttpRequestMessage? rejectReq = new(
                    HttpMethod.Post,
                    $"{ApiBase}/cs2db/rejected"
                );
                rejectReq.Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json"
                );
                rejectReq.Headers.Add("X-Api-Key", ApiKeyLocal.Value);

                HttpResponseMessage? response = await Http.SendAsync(rejectReq);
            }

            LogHelper.SendLog("Starting SubmitZips", LogLevel.DEVD);
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

            if (ModsAdded.Count > 0 && Mod.m_Setting.ConsentForUsernameShare)
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
                        case System.Net.HttpStatusCode.BadRequest:
                            CancelTask(false, "Bad request sent");
                            return;
                        case System.Net.HttpStatusCode.Unauthorized:
                            CancelTask(false, "Unauthorized request sent");
                            return;
                        default:
                            break;
                    }
                    LogHelper.SendLog(
                        $"Upload Response: {uploadResponse.StatusCode}",
                        LogLevel.DEVD
                    );
                }
            }

            int count = 0;

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

                    using HttpResponseMessage? uploadResponse = await Http.SendAsync(request);

                    if (uploadResponse != null)
                    {
                        switch (uploadResponse.StatusCode)
                        {
                            case System.Net.HttpStatusCode.BadRequest:
                                CancelTask(false, "Bad request sent");
                                return;
                            case System.Net.HttpStatusCode.Unauthorized:
                                CancelTask(false, "Unauthorized request sent");
                                return;
                            default:
                                break;
                        }
                        LogHelper.SendLog(
                            $"Upload Response: {uploadResponse.StatusCode}",
                            LogLevel.DEVD
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
                $"Collecting {ImagesNeeded.Count} icons for submission...",
                LogLevel.DEVD
            );

            foreach (string item in ImagesNeeded)
            {
                if (!AssetDatabase.global.TryGetAsset(item, out AssetData ass))
                    continue;

                string extension = Path.GetExtension(ass.id.uri);
                string guid = ass.id.guid.ToString();

                string filename = $"{guid}{extension}";
                string destPath = Path.Combine(tempDir, filename);

                try
                {
                    using Stream readStream = ass.database.GetReadStream(ass.id);
                    using (FileStream fileStream = File.Create(destPath))
                    {
                        readStream.CopyTo(fileStream);
                    }

                    using SHA256 sha256 = SHA256.Create();

                    using FileStream readBack = File.OpenRead(destPath);
                    byte[] hashBytes = sha256.ComputeHash(readBack);

                    string hash = FileHelper.Sha256FromBytes(hashBytes);
                    long size = new FileInfo(destPath).Length;

                    CollectedImages.Add(
                        new CollectedImage
                        {
                            Guid = guid,
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
                    Guid = img.Guid,
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
                .Where(img => neededSet.Contains(img.Guid))
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
                string zipName = $"~ADC_{timeNow}_images_{zipIndex}.zip";
                string zipPath = Path.Combine(baseDirectory, zipName);

                List<ImageManifestEntry> zipManifest = batch
                    .Select(img => new ImageManifestEntry
                    {
                        Guid = img.Guid,
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
                            $"{img.Guid}{img.Extension}",
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
                    File.Delete(file);

                foreach (string dir in Directory.GetDirectories(folderPath))
                    Directory.Delete(dir, recursive: true);
            }
        }

        internal void CancelTask(
            bool disabled = false,
            string log = null,
            LogLevel logLevel = LogLevel.Info
        )
        {
            Cancelled = true;
            NotificationSystem.Pop(
                notifIdentifier,
                delay: 3,
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
            {
                LogHelper.SendLog($"{log.TrimEnd('.')}. Cancelling...", logLevel);
            }
            DisabledForSession = disabled;
            TaskRunning = false;
            if (stopwatch != null && stopwatch.IsRunning)
            {
                stopwatch.Stop();
                LogHelper.SendLog($"Cancelled after {stopwatch.Elapsed}");
            }
            if (!DisabledForSession)
            {
                Colossal.Core.MainThreadDispatcher.RegisterUpdater(DispatchOnMain);
            }
        }

        internal static async Task<HashSet<string>> ValidateModFoldersAsync(
            List<string> packages,
            int validableMax
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
                        modId = modFolderParts[0],
                        modName = modFolderParts[0],
                        modVersion = modFolderParts[1],
                    };

                    try
                    {
                        if (File.Exists(metadataFile))
                        {
                            JObject jsonObject = JObject.Parse(
                                await File.ReadAllTextAsync(metadataFile)
                            );
                            modData.modName =
                                jsonObject["DisplayName"]?.ToString()
                                ?? jsonObject["displayName"]?.ToString()
                                ?? modData.modId.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.SendLog("Error reading metadata: " + ex.Message);
                    }

                    string manifestPath = ManifestHelper.FindManifestFile(subfolder);
                    if (string.IsNullOrEmpty(manifestPath))
                        continue;

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
                        if (validPackages.Count >= validableMax)
                        {
                            return validPackages;
                        }
                        continue;
                    }
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

            LogHelper.SendLog($"{files.Length} files found", LogLevel.DEVD);

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

                        string? actualHash = FileHelper.Sha256FromFile(filePath);

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

        public static void CleanLocalSource()
        {
            SourceDataCache newCache = new() { ServerTime = 0, Rows = new List<SourceDataRow>() };

            File.WriteAllText(SourceDataPath, JsonConvert.SerializeObject(newCache));

            LogHelper.SendLog("Cleanup successful");
        }
    }
}
