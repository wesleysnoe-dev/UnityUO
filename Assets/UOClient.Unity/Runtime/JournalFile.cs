using System;
using System.IO;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>
    /// The options' SaveJournalToFile (TazUO's JournalManager): while on, every journal line is appended to
    /// <c>Logs/JournalLogs/{date}_{name}_journal.txt</c> under the project (git-ignored, not UserSettings), one file per
    /// session, in TazUO's line format (GameOptions.JournalFileName / JournalLine). Nothing is opened while it is off;
    /// turning it off closes the file. Old logs are never deleted (TazUO keeps its newest 100).
    /// </summary>
    public sealed class JournalFile : IDisposable
    {
        private World _world;
        private Func<string> _name;
        private StreamWriter _writer;
        private bool _failed;
        private bool _enabled;

        public static string Folder => Path.Combine(ClientPaths.ProjectRoot, "Logs", "JournalLogs");

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (!value)
                    Close();
            }
        }

        /// <summary>A new session's journal: the next line starts a new file.</summary>
        public void Attach(World world, Func<string> characterName)
        {
            if (_world != null)
                _world.JournalAdded -= Write;
            Close();
            _failed = false;
            _world = world;
            _name = characterName;
            if (_world != null)
                _world.JournalAdded += Write;
        }

        private void Write(JournalEntry j)
        {
            if (!_enabled || _failed)
                return;
            try
            {
                if (_writer == null)
                {
                    Directory.CreateDirectory(Folder);
                    string path = Path.Combine(Folder, GameOptions.JournalFileName(DateTime.Now, _name?.Invoke()));
                    _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
                }
                _writer.WriteLine(GameOptions.JournalLine(j.Time == default ? DateTime.Now : j.Time, j.Speaker, j.Text));
            }
            catch (Exception e)
            {
                _failed = true; // one warning, then no more tries this session
                Close();
                Debug.LogWarning($"UOClient: could not write the journal log ({e.Message}).");
            }
        }

        private void Close()
        {
            _writer?.Dispose();
            _writer = null;
        }

        public void Dispose()
        {
            Attach(null, null);
        }
    }
}
