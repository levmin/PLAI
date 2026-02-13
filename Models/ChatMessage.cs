using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PLAI.Models
{
    public enum ChatRole
    {
        User,
        Assistant,
        System
    }

    public sealed class ChatMessage : INotifyPropertyChanged
    {
        private string _content;

        public ChatMessage(ChatRole role, string content)
        {
            Role = role;
            _content = content ?? string.Empty;
        }

        public ChatRole Role { get; }

        public bool IsUser => Role == ChatRole.User;

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
