#nullable disable
using MultiPresence.Infrastructure;
using MultiPresence.Runtime;
using DiscordRPC;
using System.Diagnostics;
using System.Text.Json;

namespace MultiPresence.Presence
{
    public class SKR
    {
        private const string ProcessName = "sekiro";
        private const string ConfigPath = "Assets/config/Sekiro.json";
        private const string GameTitle = "Sekiro: Shadows Die Twice";

        /// <summary>
        /// Sekiro has no Discord application of its own. Until one is created, this borrows the
        /// Sonic Adventure 2 application that already ships with MultiPresence so the presence
        /// shows up immediately; only the artwork is wrong. Put a real id in the "AppId" field of
        /// Assets/config/Sekiro.json to replace it, and see Placeholders/Sekiro.md.
        /// </summary>
        private const string FallbackApplicationId = "982562008228560986";

        private const int RipRelativeInstructionLength = 7;

        // Sanity bounds. A chain that resolved but returns nonsense outside these is wrong.
        private const int MaximumReasonableVitality = 100_000;
        private const int MaximumReasonableDeaths = 100_000_000;

        // -------------------------------------------------------------------------------------
        // Candidate locations for the global pointer that leads to the player tree.
        // Tried in order; the first one that returns sane values wins. The signatures cover
        // both the 1.06 layout and the older 1.2-1.4 layout, because the code around the
        // globals moved between game updates even though the structures did not.
        // -------------------------------------------------------------------------------------
        private static readonly PointerCandidate[] WorldChrManCandidates =
        [
            // mov rax,rsi / mov [rip+disp32],rax / test rax,rax
            new("48 8B C6 48 89 05 ?? ?? ?? ?? 48 85 C0", 3),
            // mov rbx,[rip+disp32] / test rbx,rbx / je / mov edx,[rdi] / cmp edx,0x2710
            new("48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 8B ?? 81 FA", 0)
        ];

        private static readonly PointerCandidate[] GameDataCandidates =
        [
            // movzx ebx,dl / mov rsi,rcx / ... / mov rax,[rip+disp32]
            new("0F B6 DA 4C 8B F1 40 32 FF 48 8B 05", 9),
            // The player stats reader. Self describing: the member offset is part of the match,
            // so it follows the structure instead of assuming a fixed one.
            new("0F B6 48 ?? 88 8B ?? ?? 00 00 48 8B 05 ?? ?? ?? ?? 8B 88 ?? ?? 00 00 " +
                "89 8B ?? ?? 00 00 48 8B 05 ?? ?? ?? ?? 8B 88 ?? ?? 00 00", 29)
        ];

        // Known static addresses, relative to sekiro.exe. Game 1.06 (exe 1.5.0.0) only.
        private const ulong WorldChrManRva = 0x03D7A1E0;
        private const ulong GameDataRva = 0x03D5AAC0;

        // Paths from WorldChrMan down to the structure that holds the vitals. Both variants
        // appear across builds; each one is tried against both roots above.
        private static readonly uint[][] VitalsChains =
        [
            [0x88, 0x1FF8, 0x18],
            [0x88, 0x1FF8, 0x28]
        ];

        // Path to the progression structure (sen, level, experience, attack power).
        private static readonly uint[] ProgressChain = [0x88, 0x2000];

        private const uint VitalityOffset = 0x130;
        private const uint MaxVitalityOffset = 0x134;
        private const uint PostureOffset = 0x148;
        private const uint MaxPostureOffset = 0x14C;
        private const uint LevelOffset = 0x154;
        private const uint ExperienceOffset = 0x160;
        private const uint SenOffset = 0x07C;
        private const uint AttackPowerOffset = 0x48;
        private const uint DeathCountOffset = 0x90;
        private const uint NewGamePlusOffset = 0x70;

        private static DiscordRpcClient discord;
        private static DiscordStatusUpdater updater;

        private static ResolvedRoot worldChrMan;
        private static ResolvedRoot gameData;
        private static bool pointersAttempted;

        public static void DoAction()
        {
            GetPID();
            discord = new DiscordRpcClient(ResolveApplicationId());
            InitializeDiscord();
            updater = new DiscordStatusUpdater(ConfigPath);
            PresenceRuntime.Start(nameof(SKR), ProcessName, RPC);
        }

        private static void GetPID()
        {
            try
            {
                ProcessMonitor.TryAttach(ProcessName);
            }
            catch
            {
                //nothing?
            }
        }

        private static async Task RPC()
        {
            if (ProcessMonitor.IsRunning(ProcessName))
            {
                try
                {
                    ResolvePointers();
                    if (worldChrMan is null)
                    {
                        // No layout produced sane values, so there is nothing to report yet.
                        ShowMainMenu();
                        return;
                    }

                    if (ReadValue(worldChrMan.Field(MaxVitalityOffset)) > 0)
                    {
                        var placeholders = await PlaceholderHelper.GetPlaceholders(GeneratePlaceholders);
                        PlaceholderHelper.UpdateDiscordStatus(discord, updater, "Sekiro", placeholders);
                    }
                    else
                    {
                        ShowMainMenu();
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                {
                    // The pointers are resolved and verified once, so this only runs if the game
                    // changed state underneath us. Fall back to the menu quietly, like the other
                    // presence readers do.
                    _ = exception;
                    ShowMainMenu();
                }
            }
            else
            {
                pointersAttempted = false;
                worldChrMan = null;
                gameData = null;
                discord.Deinitialize();
                updater.Dispose();
                PresenceRuntime.RequestDetection();
            }
        }

        private static async Task<Dictionary<string, object>> GeneratePlaceholders()
        {
            int hp = ReadValue(worldChrMan.Field(VitalityOffset));
            int maxhp = ReadValue(worldChrMan.Field(MaxVitalityOffset));
            int posture = ReadValue(worldChrMan.Field(PostureOffset));
            int maxposture = ReadValue(worldChrMan.Field(MaxPostureOffset));
            int level = ReadValue(worldChrMan.Base.Field(ProgressChain, LevelOffset));
            int xp = ReadValue(worldChrMan.Base.Field(ProgressChain, ExperienceOffset));
            int sen = ReadValue(worldChrMan.Base.Field(ProgressChain, SenOffset));
            int attackpower = ReadValue(worldChrMan.Base.Field(ProgressChain, AttackPowerOffset));
            int deaths = gameData is null ? 0 : ReadValue(gameData.Field(DeathCountOffset));
            int clearcount = gameData is null ? 0 : ReadValue(gameData.Field(NewGamePlusOffset));

            return new Dictionary<string, object>
            {
                { "hp", hp },
                { "maxhp", maxhp },
                { "posture", posture },
                { "maxposture", maxposture },
                { "level", level },
                { "xp", xp },
                { "sen", sen },
                { "attackpower", attackpower },
                { "deaths", deaths },
                { "clearcount", clearcount }
            };
        }

        private static int ReadValue(ulong address) => Hypervisor.Read<int>(address, true);

        private static void ShowMainMenu()
        {
            discord.SetPresence(new RichPresence()
            {
                Details = "In Main Menu",
                State = "",
                Assets = new Assets()
                {
                    LargeImageKey = "logo",
                    LargeImageText = GameTitle
                },
                Timestamps = PlaceholderHelper._startTimestamp
            });
        }

        /// <summary>
        /// Works out which layout this build of sekiro.exe uses. Each candidate is located by a
        /// byte signature and then read for real; a candidate only wins if it produces sane
        /// values. Scans are expensive, so this runs once per session and never inside the
        /// polling loop.
        /// </summary>
        private static void ResolvePointers()
        {
            if (pointersAttempted)
            {
                return;
            }

            pointersAttempted = true;

            var version = ReadGameVersion();
            AppLog.Information($"Sekiro found, game version {version}. Looking for the player layout.");

            worldChrMan = ResolveWorldChrMan();
            gameData = ResolveGameData();

            if (worldChrMan is null)
            {
                AppLog.Warning(
                    $"No known Sekiro layout produced sane vitality values on version {version}. " +
                    "The presence will stay on the main menu until a layout for this build is added.");
            }
        }

        private static ResolvedRoot ResolveWorldChrMan()
        {
            foreach (var (source, pointer) in PointerCandidates.Enumerate(WorldChrManCandidates, WorldChrManRva))
            {
                foreach (var chain in VitalsChains)
                {
                    var root = new ResolvedRoot(pointer, chain);
                    if (IsSane(root.Field(MaxVitalityOffset), MaximumReasonableVitality))
                    {
                        AppLog.Information(
                            $"Sekiro player layout accepted via {source}: root 0x{pointer.Address:X}, " +
                            $"chain [{string.Join(", ", chain)}].");
                        return root;
                    }
                }
            }

            return null;
        }

        private static ResolvedRoot ResolveGameData()
        {
            foreach (var (source, pointer) in PointerCandidates.Enumerate(GameDataCandidates, GameDataRva))
            {
                var root = new ResolvedRoot(pointer, []);
                try
                {
                    var deaths = ReadValue(root.Field(DeathCountOffset));
                    if (deaths >= 0 && deaths < MaximumReasonableDeaths)
                    {
                        AppLog.Information(
                            $"Sekiro game data layout accepted via {source}: root 0x{pointer.Address:X}.");
                        return root;
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                {
                    // Wrong candidate for this build, try the next one.
                    _ = exception;
                }
            }

            return null;
        }

        private static bool IsSane(ulong address, int bound)
        {
            try
            {
                var value = ReadValue(address);
                return value > 0 && value < bound;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // Wrong candidate for this build, try the next one.
                _ = exception;
                return false;
            }
        }

        private static string ReadGameVersion()
        {
            Process[] processes = [];
            try
            {
                processes = Process.GetProcessesByName(ProcessName);
                foreach (var process in processes)
                {
                    try
                    {
                        var version = process.MainModule.FileVersionInfo.ProductVersion;
                        if (!string.IsNullOrWhiteSpace(version))
                        {
                            return version;
                        }
                    }
                    catch
                    {
                        // Try the next instance.
                    }
                }
            }
            catch
            {
                // Fall through to unknown.
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }

            return "unknown";
        }

        private static string ResolveApplicationId()
        {
            try
            {
                var path = Path.GetFullPath(ConfigPath, AppContext.BaseDirectory);
                if (File.Exists(path))
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var document = JsonDocument.Parse(stream);
                    if (document.RootElement.TryGetProperty("AppId", out var value) &&
                        value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        return value.GetString();
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                AppLog.Warning("Could not read the Sekiro Discord application id.", exception);
            }

            return FallbackApplicationId;
        }

        private static void InitializeDiscord()
        {
            discord.Initialize();
        }

        /// <summary>
        /// A location of a global pointer. <see cref="Address"/> is either an absolute address
        /// produced by a signature scan or a module relative offset used as a fallback.
        /// </summary>
        private readonly record struct PointerBase(ulong Address, bool IsAbsolute)
        {
            public ulong Field(uint field) =>
                Hypervisor.GetPointer64(Address, [field], IsAbsolute);

            public ulong Field(uint[] chain, uint field) =>
                Hypervisor.GetPointer64(Address, [.. chain, field], IsAbsolute);
        }

        /// <summary>
        /// A global pointer plus the chain that leads from it to the structure we want.
        /// </summary>
        private sealed class ResolvedRoot(PointerBase basePointer, uint[] chain)
        {
            public PointerBase Base { get; } = basePointer;

            public uint[] Chain { get; } = chain;

            public ulong Field(uint field) => Base.Field(Chain, field);
        }

        /// <summary>
        /// A byte signature and the offset of its RIP-relative instruction inside the match.
        /// It deliberately resolves nothing: the scan needs an attached process, so it must only
        /// happen once <see cref="ResolvePointers"/> runs and never while this type is loaded.
        /// </summary>
        private sealed class PointerCandidate(string signature, int instructionOffset)
        {
            public string Signature { get; } = signature;

            public int InstructionOffset { get; } = instructionOffset;
        }

        private static class PointerCandidates
        {
            /// <summary>
            /// Yields every location worth trying, signatures first and the static 1.06
            /// address last, because that one is only correct on one specific build. A signature
            /// that cannot be found is skipped silently; the caller decides whether what it got
            /// back actually works.
            /// </summary>
            public static IEnumerable<(string Source, PointerBase Base)> Enumerate(
                PointerCandidate[] candidates,
                ulong fallbackRva)
            {
                foreach (var candidate in candidates)
                {
                    PointerBase? located = null;
                    try
                    {
                        located = Locate(candidate);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                    {
                        AppLog.Information(
                            $"Sekiro signature '{candidate.Signature}' did not resolve: {exception.Message}");
                    }

                    if (located is { } pointer)
                    {
                        yield return ("signature", pointer);
                    }
                }

                yield return ("static 1.06 address", new PointerBase(fallbackRva, false));
            }

            private static PointerBase Locate(PointerCandidate candidate)
            {
                var match = unchecked((ulong)Hypervisor.FindSignature(candidate.Signature).ToInt64());
                var instruction = match + (ulong)candidate.InstructionOffset;
                var displacement = Hypervisor.Read<int>(instruction + 3, true);
                return new PointerBase(instruction + RipRelativeInstructionLength + (ulong)displacement, true);
            }
        }
    }
}