using LiwaPlayer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LiwaPlayer.Services
{
    public class PlaylistService
    {
        private static readonly CultureInfo Turkish = new("tr-TR");

        private readonly string _playlistsFile;
        private readonly string _legacyFile;

        public ObservableCollection<Playlist> Playlists { get; } = new();

        public Playlist ActivePlaylist { get; private set; } = null!;

        public Song? CurrentSong { get; private set; }

        public PlaylistService()
        {
            _playlistsFile = Path.Combine(AppPaths.DataFolder, "Playlists.json");
            _legacyFile = Path.Combine(AppPaths.DataFolder, "Playlist.json");

            Load();
        }

        // ═══════════ Liste yönetimi ═══════════

        public bool NameExists(string name) =>
            Playlists.Any(p => string.Compare(p.Name, name, true, Turkish) == 0);

        public Playlist CreatePlaylist(string name)
        {
            var playlist = new Playlist { Name = name.Trim() };

            Playlists.Add(playlist);
            Save();

            return playlist;
        }

        public void DeletePlaylist(Playlist playlist)
        {
            if (!Playlists.Remove(playlist))
                return;

            // Hiç liste kalmazsa çalışacak bir varsayılan liste oluştur
            if (Playlists.Count == 0)
                Playlists.Add(new Playlist { Name = "Müzik" });

            if (ActivePlaylist == playlist)
                ActivePlaylist = Playlists[0];

            if (CurrentSong != null && playlist.Songs.Contains(CurrentSong))
                SetCurrent(null);

            UpdateActiveFlags();
            Save();
        }

        public void SetActive(Playlist playlist)
        {
            if (!Playlists.Contains(playlist))
                return;

            ActivePlaylist = playlist;
            UpdateActiveFlags();
        }

        // Adında "doğum" geçen ilk liste (otomatik doğum günü yönlendirmesi için).
        // Kullanıcı böyle bir listeyi silmişse null döner ve yönlendirme yapılmaz.
        public Playlist? FindBirthdayPlaylist() =>
            Playlists.FirstOrDefault(p => p.Name.ToLower(Turkish).Contains("doğum"));

        private void UpdateActiveFlags()
        {
            foreach (var p in Playlists)
                p.IsActive = p == ActivePlaylist;
        }

        // ═══════════ Şarkı işlemleri ═══════════

        // Aynı dosya/video hedef listede zaten varsa eklemez, mevcut kaydı döndürür
        public Song Add(Song song, Playlist target, out bool added)
        {
            var existing = target.Songs.FirstOrDefault(s =>
                s.Source == song.Source &&
                string.Equals(s.FileName, song.FileName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                added = false;
                return existing;
            }

            target.Songs.Add(song);
            Save();

            added = true;
            return song;
        }

        // Şarkıyı bir listeden diğerine taşır. Hedefte aynı şarkı zaten varsa
        // taşımaz ve false döner (kaynaktan da silinmez).
        public bool MoveSong(Song song, Playlist from, Playlist to)
        {
            if (from == to || !from.Songs.Contains(song))
                return false;

            bool existsInTarget = to.Songs.Any(s =>
                s.Source == song.Source &&
                string.Equals(s.FileName, song.FileName, StringComparison.OrdinalIgnoreCase));

            if (existsInTarget)
                return false;

            from.Songs.Remove(song);
            to.Songs.Add(song);
            Save();

            return true;
        }

        public void RemoveFromActive(Song song)
        {
            if (!ActivePlaylist.Songs.Remove(song))
                return;

            if (CurrentSong == song)
                SetCurrent(null);

            Save();
        }

        public void ClearActive()
        {
            bool currentWasHere = CurrentSong != null && ActivePlaylist.Songs.Contains(CurrentSong);

            ActivePlaylist.Songs.Clear();

            if (currentWasHere)
                SetCurrent(null);

            Save();
        }

        public void SetCurrent(Song? song)
        {
            foreach (var p in Playlists)
                foreach (var s in p.Songs)
                    s.IsCurrent = false;

            CurrentSong = song;

            if (CurrentSong != null)
                CurrentSong.IsCurrent = true;
        }

        // ═══════════ Gezinme (hep tek liste içinde döner) ═══════════

        public Song? Next(Playlist playlist)
        {
            var songs = playlist.Songs;

            if (songs.Count == 0)
                return null;

            int index = CurrentSong != null ? songs.IndexOf(CurrentSong) : -1;

            if (index < 0)
                return songs[0];

            index++;

            if (index >= songs.Count)
                index = 0;

            return songs[index];
        }

        public Song? Previous(Playlist playlist)
        {
            var songs = playlist.Songs;

            if (songs.Count == 0)
                return null;

            int index = CurrentSong != null ? songs.IndexOf(CurrentSong) : -1;

            if (index < 0)
                return songs[0];

            index--;

            if (index < 0)
                index = songs.Count - 1;

            return songs[index];
        }

        // ═══════════ Kalıcılık ═══════════

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(
                    Playlists.ToList(),
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_playlistsFile, json);
            }
            catch
            {
                // Diske yazılamazsa listeler bellekte çalışmaya devam eder
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_playlistsFile))
                {
                    var json = File.ReadAllText(_playlistsFile);

                    var list = JsonSerializer.Deserialize<List<Playlist>>(json);

                    if (list != null)
                        foreach (var p in list)
                            Playlists.Add(p);
                }
                else if (File.Exists(_legacyFile))
                {
                    MigrateFromLegacy();
                }
            }
            catch
            {
                // Bozuk dosya uygulamayı düşürmesin, varsayılanlarla devam
            }

            if (Playlists.Count == 0)
            {
                Playlists.Add(new Playlist { Name = "Müzik" });
                Playlists.Add(new Playlist { Name = "Doğum Günü" });
                Save();
            }

            ActivePlaylist = Playlists[0];
            UpdateActiveFlags();
        }

        // Eski tek-liste format (Playlist.json, Category alanlı) yeni yapıya taşınır
        private void MigrateFromLegacy()
        {
            var json = File.ReadAllText(_legacyFile);

            var songs = JsonSerializer.Deserialize<List<Song>>(json);

            if (songs == null)
                return;

            var music = new Playlist { Name = "Müzik" };
            var birthday = new Playlist { Name = "Doğum Günü" };

            foreach (var song in songs)
            {
                if (song.Category == SongCategory.Birthday)
                    birthday.Songs.Add(song);
                else
                    music.Songs.Add(song);
            }

            Playlists.Add(music);
            Playlists.Add(birthday);

            Save();
        }
    }
}
