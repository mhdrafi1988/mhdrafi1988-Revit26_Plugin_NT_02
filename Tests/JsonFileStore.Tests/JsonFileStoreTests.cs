using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.Shared.Services;
using Xunit;
using Store = Revit26_Plugin_Revised.Shared.Services.JsonFileStore;

namespace JsonFileStore.Tests
{
    /// <summary>Stand-in for a tool's memory file: named sets.</summary>
    public sealed class TestFile
    {
        public int FormatVersion { get; set; } = 1;
        public List<string> Sets { get; set; } = new();
    }

    public sealed class JsonFileStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "JsonFileStoreTests_" + Guid.NewGuid().ToString("N"));
        private readonly string _path;

        public JsonFileStoreTests()
        {
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "memory.json");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private static Func<TestFile, bool> Add(string name) => f => { f.Sets.Add(name); return true; };

        [Fact]
        public void Saved_in_one_session_loads_in_the_next()
        {
            Store.Update<TestFile>(_path, Add("Set A"));

            TestFile next = Store.Load<TestFile>(_path, out string warning);

            Assert.Null(warning);
            Assert.Equal(new[] { "Set A" }, next.Sets);
        }

        [Fact]
        public void Update_keeps_what_another_session_saved_meanwhile()
        {
            Store.Update<TestFile>(_path, Add("Old"));

            // Both sessions open their window (load) before either saves.
            TestFile sessionA = Store.Load<TestFile>(_path, out _);
            TestFile sessionB = Store.Load<TestFile>(_path, out _);
            Assert.Single(sessionA.Sets);
            Assert.Single(sessionB.Sets);

            Store.Update<TestFile>(_path, Add("From B"));
            TestFile afterA = Store.Update<TestFile>(_path, Add("From A"));

            Assert.Equal(new[] { "Old", "From B", "From A" }, afterA.Sets);
            Assert.Equal(afterA.Sets, Store.Load<TestFile>(_path, out _).Sets);
        }

        [Fact]
        public void Update_that_changes_nothing_does_not_write()
        {
            TestFile result = Store.Update<TestFile>(_path, _ => false);

            Assert.Empty(result.Sets);
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public void Concurrent_updates_all_survive()
        {
            const int sessions = 8, savesEach = 5;
            Parallel.For(0, sessions, s =>
            {
                for (int i = 0; i < savesEach; i++)
                    Store.Update<TestFile>(_path, Add($"{s}-{i}"));
            });

            TestFile file = Store.Load<TestFile>(_path, out string warning);
            Assert.Null(warning);
            Assert.Equal(sessions * savesEach, file.Sets.Distinct().Count());
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void Locked_file_is_reported_and_never_overwritten()
        {
            Store.Update<TestFile>(_path, Add("Keep me"));
            string before = File.ReadAllText(_path);

            using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                TestFile loaded = Store.Load<TestFile>(_path, out string warning);
                Assert.NotNull(warning);
                Assert.Empty(loaded.Sets);

                Assert.ThrowsAny<IOException>(() =>
                    Store.Update<TestFile>(_path, Add("New")));
            }

            Assert.Equal(before, File.ReadAllText(_path));
            Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*"));
        }

        [Fact]
        public async Task File_unlocked_within_the_retry_time_is_read()
        {
            Store.Update<TestFile>(_path, Add("Keep me"));

            var gate = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Task release = Task.Delay(300).ContinueWith(_ => gate.Dispose());

            TestFile loaded = Store.Load<TestFile>(_path, out string warning);
            await release;

            Assert.Null(warning);
            Assert.Equal(new[] { "Keep me" }, loaded.Sets);
        }

        [Fact]
        public void Corrupt_file_is_kept_aside_and_a_new_one_started()
        {
            File.WriteAllText(_path, "{ not json");

            TestFile loaded = Store.Load<TestFile>(_path, out string warning);
            Assert.NotNull(warning);
            Assert.Empty(loaded.Sets);
            Assert.Single(Directory.GetFiles(_dir, "*.corrupt-*"));

            TestFile saved = Store.Update<TestFile>(_path, Add("Fresh"));
            Assert.Equal(new[] { "Fresh" }, saved.Sets);
        }

        [Fact]
        public void Settings_with_a_file_name_are_kept_apart()
        {
            string tool = "JsonFileStoreTests_" + Guid.NewGuid().ToString("N");
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Revit26_Plugin_Revised", tool);
            try
            {
                SettingsService<TestFile>.Save(tool, "settings_v001.json", new TestFile { Sets = { "v1" } });
                SettingsService<TestFile>.Save(tool, "settings_v002.json", new TestFile { Sets = { "v2" } });

                Assert.Equal(new[] { "v1" }, SettingsService<TestFile>.Load(tool, "settings_v001.json").Sets);
                Assert.Equal(new[] { "v2" }, SettingsService<TestFile>.Load(tool, "settings_v002.json").Sets);
                Assert.Empty(SettingsService<TestFile>.Load(tool).Sets);   // settings.json untouched
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (IOException) { }
            }
        }
    }
}
