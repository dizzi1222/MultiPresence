#nullable disable
using MultiPresence.Infrastructure;
using MultiPresence.Runtime;
using DiscordRPC;
using System.Text.Json;

namespace MultiPresence.Presence
{
    public class SKR
    {
        private const string ProcessName = "sekiro";
        private const string ConfigPath = "Assets/config/Sekiro.json";
        private const string GameTitle = "Sekiro: Shadows Die Twice";

        /// <summary>
        /// Used only when <c>Assets/config/Sekiro.json</c> has no "AppId" entry.
        /// Create your own application at https://discord.com/developers/applications and
        /// write its 19 digit id into the config to see the presence.
        /// </summary>
        private const string FallbackApplicationId = "0000000000000000000";

        // Address of the "mov [rip+disp32], rax" instruction inside the signature below.
        private const int WorldChrManInstructionOffset = 3;
        private const string WorldChrManSignature = "48 8B C6 48 89 05 ?? ?? ?? ?? 48 85 C0";

        // Address of the "mov rax, [rip+disp32]" instruction inside the signature below.
        private const int GameDataInstructionOffset = 9;
        private const string GameDataSignature = "0F B6 DA 4C 8B F1 40 32 FF 48 8B 05";

        private const int RipRelativeInstructionLength = 7;

        // Fallback addresses, relative to sekiro.exe. Valid for game 1.06 (exe 1.5.0.0).
        private const ulong WorldChrManRva = 0x03D7A1E0;
        private const ulong GameDataRva = 0x03D5AAC0;

        // WorldChrMan -> PlayerStats: dereferenced before the field offset is applied.
        private static readonly uint[] PlayerVitalsChain = [0x88, 0x1FF8, 0x18];
        private static readonly uint[] PlayerProgressChain = [0x88, 0x2000];

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

        /// <summary>
        /// Upper bound used to reject a pointer chain that resolved to nonsense. Max vitality in
        /// Sekiro is a few hundred even after upgrades, so anything past this is a bad read.
        /// </summary>
        private const int MaximumReasonableVitality = 100000;

        private static DiscordRpcClient discord;
        private static DiscordStatusUpdater updater;

        private static PointerBase worldChrMan;
        private static PointerBase gameData;
        private static bool pointersResolved;

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
                    int maxhp = ReadValue(worldChrMan.Field(PlayerVitalsChain, MaxVitalityOffset));

                    if (maxhp > 0)
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
                pointersResolved = false;
                discord.Deinitialize();
                updater.Dispose();
                PresenceRuntime.RequestDetection();
            }
        }

        private static async Task<Dictionary<string, object>> GeneratePlaceholders()
        {
            int hp = ReadValue(worldChrMan.Field(PlayerVitalsChain, VitalityOffset));
            int maxhp = ReadValue(worldChrMan.Field(PlayerVitalsChain, MaxVitalityOffset));
            int posture = ReadValue(worldChrMan.Field(PlayerVitalsChain, PostureOffset));
            int maxposture = ReadValue(worldChrMan.Field(PlayerVitalsChain, MaxPostureOffset));
            int level = ReadValue(worldChrMan.Field(PlayerProgressChain, LevelOffset));
            int xp = ReadValue(worldChrMan.Field(PlayerProgressChain, ExperienceOffset));
            int sen = ReadValue(worldChrMan.Field(PlayerProgressChain, SenOffset));
            int attackpower = ReadValue(worldChrMan.Field(PlayerProgressChain, AttackPowerOffset));
            int deaths = ReadValue(gameData.Field(DeathCountOffset));
            int clearcount = ReadValue(gameData.Field(NewGamePlusOffset));

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
        /// Scans for the two global pointers once per session and verifies each one before
        /// accepting it. The scan walks every readable region of sekiro.exe, so it must never
        /// run inside the polling loop, and a bad chain must be rejected here rather than
        /// throwing on every poll.
        /// </summary>
        private static void ResolvePointers()
        {
            if (pointersResolved)
            {
                return;
            }

            var worldChrManStatic = new PointerBase(WorldChrManRva, false);
            var worldChrManScanned = PointerBase.Resolve(
                WorldChrManSignature,
                WorldChrManInstructionOffset,
                WorldChrManRva);

            var gameDataStatic = new PointerBase(GameDataRva, false);
            var gameDataScanned = PointerBase.Resolve(
                GameDataSignature,
                GameDataInstructionOffset,
                GameDataRva);

            worldChrMan = Verify(worldChrManScanned, worldChrManStatic, PlayerVitalsChain, MaxVitalityOffset);
            gameData = Verify(gameDataScanned, gameDataStatic, DeathCountOffset);
            pointersResolved = true;
        }

        private static PointerBase Verify(PointerBase scanned, PointerBase fallback, uint[] chain, uint field)
        {
            try
            {
                var address = scanned.Field(chain, field);
                var value = Hypervisor.Read<int>(address, true);

                // If the chain resolved at all the address is already readable, so the only
                // useful check is on the value: max vitality has to look like a real number.
                if (value > 0 && value < MaximumReasonableVitality)
                {
                    return scanned;
                }

                AppLog.Warning(
                    $"The Sekiro signature resolved to {scanned.Address:X} but it read {value} at " +
                    $"{address:X}, which is not a sane value; falling back to the static 1.06 address.");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                AppLog.Warning(
                    "The Sekiro signature resolved but the pointer chain could not be read; " +
                    "falling back to the static 1.06 address.",
                    exception);
            }

            return fallback;
        }

        private static PointerBase Verify(PointerBase scanned, PointerBase fallback, uint field) =>
            Verify(scanned, fallback, [], field);

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
        /// A resolved location of a global pointer. <see cref="Address"/> is either an absolute
        /// address found by a signature scan or a module relative offset used as a fallback.
        /// </summary>
        private readonly record struct PointerBase(ulong Address, bool IsAbsolute)
        {
            public ulong Field(uint field) =>
                Hypervisor.GetPointer64(Address, [field], IsAbsolute);

            public ulong Field(uint[] chain, uint field) =>
                Hypervisor.GetPointer64(Address, [.. chain, field], IsAbsolute);

            public static PointerBase Resolve(string signature, int instructionOffset, ulong fallbackRva)
            {
                try
                {
                    var match = unchecked((ulong)Hypervisor.FindSignature(signature).ToInt64());
                    var instruction = match + (ulong)instructionOffset;
                    var displacement = Hypervisor.Read<int>(instruction + 3, true);
                    var target = instruction + RipRelativeInstructionLength + (ulong)displacement;

                    if (Hypervisor.TryRead<ulong>(target, out var pointer, true) && pointer != 0)
                    {
                        return new PointerBase(target, true);
                    }

                    AppLog.Warning(
                        $"The Sekiro signature '{signature}' matched at {match:X} but the pointer it " +
                        "holds is still null; using the static 1.06 address instead.");
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                {
                    AppLog.Warning(
                        $"Could not locate '{signature}' in sekiro.exe; using the static 1.06 address instead.",
                        exception);
                }

                return new PointerBase(fallbackRva, false);
            }
        }
    }
}