using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace LiwaPlayer.Models
{
    public class Playlist : INotifyPropertyChanged
    {
        private bool _isActive;

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = "";

        public ObservableCollection<Song> Songs { get; set; } = new();

        [JsonIgnore]
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;

                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => Name;
    }
}
