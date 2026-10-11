using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using MelonLoader;

[assembly: MelonInfo(typeof(TavernEssentials.Main), TavernEssentials.Mod.Name, TavernEssentials.Mod.Version, "Shadow")]
[assembly: MelonGame("Alta", "A Township Tale")]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]

namespace TavernEssentials
{
    public class Main : MelonMod
    {
        bool _social;
        bool _board;
        bool _discord;
        readonly TickFailures _socialFails = new TickFailures(Social.Log.Out);
        readonly TickFailures _boardFails = new TickFailures(Board.Log.Out);
        readonly TickFailures _discordFails = new TickFailures(Discord.Log.Out);

        public override void OnInitializeMelon()
        {
            MelonLogger.Instance log = LoggerInstance;
            Bind(Social.Log.Out, log, "Social: ");
            Bind(Board.Log.Out, log, "Board: ");
            Bind(Backend.Log.Out, log, "Backend: ");
            Bind(JoinGuard.Log.Out, log, "JoinGuard: ");
            Bind(LogGuard.Log.Out, log, "LogGuard: ");
            Bind(Names.Log.Out, log, "Names: ");
            Bind(Postbox.Log.Out, log, "Postbox: ");
            Bind(ModSettings.Log.Out, log, "Mods: ");
            Bind(Discord.Log.Out, log, "Discord: ");
            try
            {
                string mods = ModsDir();
                string game = Directory.GetParent(mods).FullName;
                bool server = IsServer();
                LoadSettings(game, mods, log);

                _social = Start(log, Social.Log.Out, "Social", "TavernSocial",
                    delegate { return Social.SocialFeature.Start(game, server); });
                if (server)
                {
                    Board.Log.Msg("game client only, so it stays off on a server.");
                    Backend.Log.Msg("game client only, so it stays off on a server.");
                    ModSettings.Log.Msg("game client only, so it stays off on a server.");
                    Start(log, Discord.Log.Out, "Discord", null, Discord.DiscordFeature.StartServer);
                    Start(log, LogGuard.Log.Out, "LogGuard", null, LogGuard.LogGuardFeature.Start);
                    Start(log, JoinGuard.Log.Out, "JoinGuard", null, JoinGuard.JoinGuardFeature.Start);
                    Start(log, Names.Log.Out, "Names", null, Names.NamesFeature.Start);
                    Start(log, Postbox.Log.Out, "Postbox", null, Postbox.PostboxFeature.Start);
                }
                else
                {
                    _board = Start(log, Board.Log.Out, "Board", "TavernBoard", Board.BoardFeature.Start);
                    Start(log, Backend.Log.Out, "Backend", null, delegate { return Backend.BackendFeature.Start(game); });
                    JoinGuard.Log.Msg("server only, so it stays off on a game client.");
                    LogGuard.Log.Msg("server only, so it stays off on a game client.");
                    Names.Log.Msg("server only, so it stays off on a game client.");
                    Start(log, Postbox.Log.Out, "Postbox", null, Postbox.PostboxFeature.StartClient);
                    Start(log, ModSettings.Log.Out, "ModSettings", null, ModSettings.ModSettingsFeature.Start);
                    _discord = Start(log, Discord.Log.Out, "Discord", null, Discord.DiscordFeature.Start);
                }
            }
            catch (Exception e)
            {
                log.Error("start-up failed, so " + Mod.Name + " is doing nothing: " + e);
            }
        }

        public override void OnUpdate()
        {
            double now = UnityEngine.Time.realtimeSinceStartup;
            if (_social)
            {
                try
                {
                    Social.SocialFeature.Tick();
                    _socialFails.Quiet(now);
                }
                catch (Exception e)
                {
                    _socialFails.Failed(e, now);
                }
            }
            if (_board)
            {
                try
                {
                    Board.BoardFeature.Tick();
                    _boardFails.Quiet(now);
                }
                catch (Exception e)
                {
                    _boardFails.Failed(e, now);
                }
            }
            if (_discord)
            {
                try
                {
                    Discord.DiscordFeature.Tick();
                    _discordFails.Quiet(now);
                }
                catch (Exception e)
                {
                    _discordFails.Failed(e, now);
                }
            }
        }

        static bool IsServer()
        {
            return ApplicationManager.IsHeadless
                || CommandLineArguments.Contains(CommandLineArguments.StartServerArgument);
        }

        static void LoadSettings(string game, string mods, MelonLogger.Instance log)
        {
            string userData = Path.Combine(game, "UserData");
            List<string> notes = new List<string>();
            Settings s = Settings.Load(
                Path.Combine(Path.Combine(userData, "TavernEssentials"), "config.txt"),
                Template(),
                new Settings.OldFile[]
                {
                    new Settings.OldFile(Path.Combine(Path.Combine(userData, "TavernSocial"), "config.txt"), Social.Config.Prefix),
                    new Settings.OldFile(Path.Combine(mods, "TavernBoard_config.txt"), Board.BoardConfig.Prefix),
                },
                notes);
            Social.Config.Current = Social.Config.From(s);
            Board.BoardConfig.Load(s);
            Backend.BackendConfig.Load(s);
            JoinGuard.JoinGuardConfig.Load(s, notes);
            LogGuard.LogGuardConfig.Load(s, notes);
            Names.NamesConfig.Load(s);
            Postbox.PostboxConfig.Load(s);
            ModSettings.ModsConfig.Load(s);
            Discord.DiscordConfig.Load(s);
            foreach (string n in notes) log.Msg(n);
        }

        internal static string[] Template()
        {
            List<string> t = new List<string>();
            t.Add("# " + Mod.Name + " settings. A line you delete comes back at its default on the next start.");
            t.Add("# One file for every feature, on a game client and on a server alike.");
            t.Add("");
            t.AddRange(Social.Config.Template);
            t.Add("");
            t.AddRange(Board.BoardConfig.Template);
            t.Add("");
            t.AddRange(Backend.BackendConfig.Template);
            t.Add("");
            t.AddRange(JoinGuard.JoinGuardConfig.Template);
            t.Add("");
            t.AddRange(LogGuard.LogGuardConfig.Template);
            t.Add("");
            t.AddRange(Names.NamesConfig.Template);
            t.Add("");
            t.AddRange(Postbox.PostboxConfig.Template);
            t.Add("");
            t.AddRange(ModSettings.ModsConfig.Template);
            t.Add("");
            t.AddRange(Discord.DiscordConfig.Template);
            return t.ToArray();
        }

        static bool Start(MelonLogger.Instance log, FeatureLog flog, string name, string oldDll, Func<bool> start)
        {
            if (oldDll != null && StillLoaded(oldDll, log))
            {
                log.Warning(oldDll + ".dll is still in Mods: delete it; " + Mod.Name + "' " + name
                    + " feature is off until you do.");
                return false;
            }
            try
            {
                return start();
            }
            catch (Exception e)
            {
                flog.Error("start-up failed, so " + name + " is off: " + e);
                return false;
            }
        }

        static bool StillLoaded(string name, MelonLogger.Instance log)
        {
            try
            {
                foreach (MelonBase m in MelonBase.RegisteredMelons)
                    if (m != null && m.Info != null && m.Info.Name == name) return true;
                foreach (MelonAssembly a in MelonAssembly.LoadedAssemblies)
                    if (a != null && a.Assembly != null && a.Assembly.GetName().Name == name) return true;
            }
            catch (Exception e)
            {
                log.Warning("could not check whether " + name + ".dll is still loaded: " + e.Message);
            }
            return false;
        }

        static void Bind(FeatureLog f, MelonLogger.Instance log, string prefix)
        {
            f.Info = delegate(string s) { log.Msg(prefix + s); };
            f.Warning = delegate(string s) { log.Warning(prefix + s); };
            f.Failure = delegate(string s) { log.Error(prefix + s); };
        }

        static string ModsDir()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }
    }
}
