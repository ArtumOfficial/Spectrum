using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Spectrum.API;
using Spectrum.API.Game;
using Spectrum.API.Game.Vehicle;
using Spectrum.API.Interfaces.Plugins;
using Spectrum.API.Interfaces.Systems;
using Spectrum.API.Configuration;
using Spectrum.API.Game.EventArgs.Vehicle;

namespace Spectrum.Plugins.SplitTimes
{
    public class Entry : IPlugin
    {
        public string FriendlyName => "Split times";
        public string Author => "Jonathan Vollebregt";
        public string Contact => "jnvsor@gmail.com";
        public APILevel CompatibleAPILevel => APILevel.UltraViolet;

        private readonly List<SplitTime> _previousCheckpointTimes = new List<SplitTime>();
        private Dictionary<int, SplitTime> _bestCheckpointTimes;
        private TimeSpan _bestTime = TimeSpan.Zero;
        private string _trackFolder;

        private FileSystem FileSystem;
        private Settings Settings;

        private bool _finished = true;

        public void Initialize(IManager manager)
        {
            LocalVehicle.CheckpointPassed += CheckpointPassed;
            LocalVehicle.Finished += Finished;
            Race.Started += Started;

            FileSystem = new FileSystem(typeof(Entry));
            Settings = new Settings(typeof(Entry));
            ValidateSettings();

            manager.Hotkeys.Bind(Settings["ShowTimesHotkey"] as string, ShowPressed, false);
        }

        private void Started(object sender, EventArgs e)
        {
            _finished = false;

            _previousCheckpointTimes.Clear();
            _bestCheckpointTimes?.Clear();
            _bestTime = TimeSpan.Zero;

            if (Settings.GetItem<bool>("SaveTimes"))
            {
                _trackFolder = SplitTime.GetSavePath(
                    G.Sys.GameManager_.Level_,
                    G.Sys.GameManager_.Mode_,
                    G.Sys.PlayerManager_.Current_.profile_
                );

                _bestCheckpointTimes = ReadTimes("pb.txt");
                if (_bestCheckpointTimes.ContainsKey(-1))
                    _bestTime = _bestCheckpointTimes[-1].Total;
            }
        }

        private void Finished(object sender, FinishedEventArgs e)
        {
            if (e.Type != RaceEndType.Finished)
                return;

            _finished = true;

            var finished = new SplitTime(_previousCheckpointTimes.LastOrDefault(), TimeSpan.FromMilliseconds(e.FinalTime), -1);
            _previousCheckpointTimes.Add(finished);

            if (Settings.GetItem<bool>("SaveTimes"))
            {
                if (_bestTime == TimeSpan.Zero || finished.Total < _bestTime)
                    WriteTimes("pb.txt");
                if (Settings.GetItem<bool>("SaveAllTimes"))
                    WriteTimes(finished.RenderFilename());
            }
        }

        private void CheckpointPassed(object sender, CheckpointHitEventArgs e)
        {
            var now = new SplitTime(_previousCheckpointTimes.LastOrDefault(), Race.ElapsedTime, e.CheckpointIndex);
            _previousCheckpointTimes.Add(now);

            if (_bestCheckpointTimes.ContainsKey(e.CheckpointIndex))
                now.SetTimeBarText(_bestCheckpointTimes[e.CheckpointIndex], 1.25f);
            else
                now.SetTimeBarText(1.25f);

            var times = GetTimeStrings();
            times.Insert(0, "<size=57><color=#6be584ff>Regenerating</color></size>");
            HudLinesDownward(2.0f, times);
        }

        private void ShowPressed()
        {
            if (G.Sys.GameManager_.IsModeGo_ && !G.Sys.GameManager_.PauseMenuOpen_ && !_finished && !G.Sys.GameManager_.Mode_.IsChatWindowOpen_)
            {
                Show();
            }
        }

        private void Show()
        {
            var times = GetTimeStrings();
            times.Insert(0, new SplitTime(_previousCheckpointTimes.LastOrDefault(), Race.ElapsedTime, 0).RenderHud());
            times.Insert(0, "<size=57><color=#00000000>Regenerating</color></size>"); // Dummy placeholder to keep positioning
            HudLinesDownward(2.5f, times);
        }

        private void HudLinesDownward(float delay, List<string> lines)
        {
            var output = new StringBuilder();

            output.Append(string.Join(Environment.NewLine, lines.ToArray()));
            for (int i = 0; i < lines.Count; i++)
                output.Insert(0, Environment.NewLine);

            LocalVehicle.HUD.Clear();
            LocalVehicle.HUD.SetHUDText(output.ToString(), delay);
        }

        private List<string> GetTimeStrings()
        {
            var l = new List<string>();

            for (int i = _previousCheckpointTimes.Count; i > 0; i--)
            {
                var split = _previousCheckpointTimes[i - 1];
                if (_bestCheckpointTimes.ContainsKey(split.CheckpointId))
                    l.Add(split.RenderHud(_bestCheckpointTimes[split.CheckpointId]));
                else
                    l.Add(split.RenderHud());
            }

            return l;
        }

        private Dictionary<int, SplitTime> ReadTimes(string filename)
        {
            var output = new Dictionary<int, SplitTime>();

            try
            {
                if (File.Exists(Path.Combine(FileSystem.DirectoryPath, Path.Combine(_trackFolder, filename))))
                {
                    using (var sr = new StreamReader(Path.Combine(FileSystem.DirectoryPath, Path.Combine(_trackFolder, filename))))
                    {
                        string[] line;
                        var oldCheckpoint = -1;

                        while ((line = sr.ReadLine()?.Split('\t')) != null)
                        {
                            var total = TimeSpan.Parse("00:" + line[0]);
                            var split = TimeSpan.Parse("00:" + line[1]);
                            var checkpoint = -1;
                            if (line.Length == 3)
                                checkpoint = int.Parse(line[2]);

                            output[checkpoint] = new SplitTime(total - split, total, checkpoint, oldCheckpoint);

                            oldCheckpoint = checkpoint;
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Spectrum.Plugins.SplitTimes: Couldn't load times. Exception below:\n{ex}");
            }

            return output;
        }

        private void WriteTimes(string filename)
        {
            try
            {
                FileSystem.CreateDirectory(_trackFolder);
                using (var sw = new StreamWriter(FileSystem.CreateFile(Path.Combine(_trackFolder, filename), true)))
                {
                    foreach (SplitTime time in _previousCheckpointTimes)
                    {
                        sw.WriteLine(time.RenderSave());
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Spectrum.Plugins.SplitTimes: Couldn't save times. Exception below:\n{ex}");
            }
        }

        private void ValidateSettings()
        {
            if (!Settings.ContainsKey("ShowTimesHotkey"))
                Settings["ShowTimesHotkey"] = "LeftControl+Z";

            if (!Settings.ContainsKey("SaveTimes"))
                Settings["SaveTimes"] = true;

            if (!Settings.ContainsKey("SaveAllTimes"))
                Settings["SaveAllTimes"] = false;

            Settings.Save();
        }

        public void Shutdown()
        {
        }
    }
}
