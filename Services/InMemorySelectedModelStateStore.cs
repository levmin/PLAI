namespace PLAI.Services
{
    // Simple in-memory implementation used by default
    public class InMemorySelectedModelStateStore : ISelectedModelStateStore
    {
        private string? _id;

        public bool TryLoadSelectedModelId(out string? id)
        {
            id = _id;
            return id != null;
        }

        public void SaveSelectedModelId(string id)
        {
            _id = id;
        }

        public void Clear()
        {
            _id = null;
        }
    }
}
