#nullable disable
using MultiPresence.Infrastructure;
using MultiPresence.Runtime;
using DiscordRPC;
using System.Diagnostics;
using System.Text.Json;

namespace MultiPresence.Presence
{
    /// <summary>
    /// Bloodborne running through bbport, a shadPS4 derived host. The game itself lives in a
    /// child process called bb-probe.exe: Bloodborne.exe is only the launcher, and although it
    /// reserves the whole guest address space it never commits it, so reading that process
    /// finds nothing but an empty reservation.
    /// </summary>
    public class BB
    {
        private const string ProcessName = "bb-probe";
        private const string ConfigPath = "Assets/config/Bloodborne.json";

        /// <summary>
        /// bbport maps the PlayStation 4 address space into the host by adding this bias to every
        /// guest offset, so guest offset 0 is committed at 0x1_0000_0000.
        /// </summary>
        private const ulong GuestMemoryBias = 0x1_0000_0000;

        /// <summary>
        /// Bloodborne has no Discord application of its own. Until one is created, this borrows
        /// the Sonic Adventure 2 application that already ships with MultiPresence so the
        /// presence shows up immediately; only the artwork is wrong. Put a real id in the
        /// "AppId" field of Assets/config/Bloodborne.json to replace it.
        /// </summary>
        private const string FallbackApplicationId = "982562008228560986";

        private static DiscordRpcClient discord;
        private static DiscordStatusUpdater updater;

        private static bool inspected;
        private static bool booted;

        public static void DoAction()
        {
            GetPID();
            discord = new DiscordRpcClient(ResolveApplicationId());
            InitializeDiscord();
            updater = new DiscordStatusUpdater(ConfigPath);
            PresenceRuntime.Start(nameof(BB), ProcessName, RPC);
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
                    InspectGuestMemory();
                    var placeholders = await PlaceholderHelper.GetPlaceholders(GeneratePlaceholders);
                    PlaceholderHelper.UpdateDiscordStatus(discord, updater, "Bloodborne", placeholders);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                {
                    _ = exception;
                    ShowLauncher();
                }
            }
            else
            {
                inspected = false;
                booted = false;
                discord.Deinitialize();
                updater.Dispose();
                PresenceRuntime.RequestDetection();
            }
        }

        private static async Task<Dictionary<string, object>> GeneratePlaceholders()
        {
            return new Dictionary<string, object>
            {
                { "state", booted ? "In Game" : "At Launcher" }
            };
        }

        /// <summary>
        /// bbport commits the guest image only once the game is actually running. Probing for it
        /// tells the launcher apart from the game, and it is done once per session because the
        /// process holds several GB of guest memory.
        /// </summary>
        private static void InspectGuestMemory()
        {
            if (inspected)
            {
                return;
            }

            inspected = true;
            booted = TryReadGuestImage();
            AppLog.Information(booted
                ? "Bloodborne guest image is committed, the game is running."
                : "Bloodborne host is up but no guest image is committed yet.");
        }

        private static bool TryReadGuestImage()
        {
            try
            {
                Hypervisor.Read<ulong>(GuestMemoryBias, true);
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                _ = exception;
                return false;
            }
        }

        private static void ShowLauncher()
        {
            discord.SetPresence(new RichPresence()
            {
                Details = "Bloodborne",
                State = "At Launcher",
                Assets = PlaceholderHelper.CreateAssets(updater, "Bloodborne", new Dictionary<string, object>()),
                Timestamps = PlaceholderHelper._startTimestamp
            });
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
                AppLog.Warning("Could not read the Bloodborne Discord application id.", exception);
            }

            return FallbackApplicationId;
        }

        private static void InitializeDiscord()
        {
            discord.Initialize();
        }
    }
}
