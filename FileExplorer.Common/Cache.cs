using System;
using System.IO;
using LiteDB;

namespace FileExplorer.Common
{
    public class Cache
    {
        public static LiteDatabase Database { get; }

        public static ILiteStorage<string> Storage { get; }

        static Cache()
        {
            string cacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache");
            Directory.CreateDirectory(cacheDirectory);

            string connectionString = $"Filename={Path.Combine(cacheDirectory, "Cache.db")}; Upgrade=true";
            Database = new LiteDatabase(connectionString);

            Storage = Database.GetStorage<string>("Thumbs");
        }
    }
}
