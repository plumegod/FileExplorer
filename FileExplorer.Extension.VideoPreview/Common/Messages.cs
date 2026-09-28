using static FileExplorer.Extension.VideoPreview.VideoPreviewSettings;

namespace FileExplorer.Extension.VideoPreview
{
    public class SettingsChangedMessage
    {
        public SettingsChangedMessage(Settings oldSettings)
        {
            OldSettings = oldSettings;
        }

        public Settings OldSettings { get; private set; }
    }
}
