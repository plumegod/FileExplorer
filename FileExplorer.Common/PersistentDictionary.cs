using LiteDB;

namespace FileExplorer.Common
{
    public class Entry<TKey, TValue>
    {
        public TKey Id { get; }
        public TValue Value { get; set; }

        public Entry()
        {
        }

        public Entry(TKey id, TValue value)
        {
            Id = id; 
            Value = value;
        }
    }

    public class PersistentDictionary<TKey, TValue>
    {
        public ILiteCollection<Entry<TKey, TValue>> Collection { get; }

        public PersistentDictionary(string name)
        {
            Collection = Cache.Database.GetCollection<Entry<TKey, TValue>>(name);
        }

        public int Count => Collection.Count();

        public TValue this[TKey key]
        {
            get
            {
                Entry<TKey, TValue> entry = Collection.FindById(new BsonValue(key));
                return entry == null ? default : entry.Value;
            }
            set => Add(new Entry<TKey, TValue>(key, value));
        }

        public void Add(Entry<TKey, TValue> item)
        {
            Collection.Upsert(item);
        }

        public void Clear()
        {
            Collection.DeleteAll();
        }

        public bool ContainsKey(TKey key)
        {
            return this[key] != null;
        }

        public bool Remove(TKey key)
        {
            return Collection.Delete(new BsonValue(key));
        }
    }
}
