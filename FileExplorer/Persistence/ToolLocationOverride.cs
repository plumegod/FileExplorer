using System.ComponentModel.DataAnnotations;

namespace FileExplorer.Persistence
{
    public sealed class ToolLocationOverride : PersistentItem
    {
        [Required]
        public string ExecutableRole { get; set; }

        [Required]
        public string ExecutablePath { get; set; }
    }
}
