namespace PLAI.Services
{
    public interface ISelectedModelStateStore
    {
        /// <summary>
        /// Try to load a previously saved selected model id.
        /// Returns true when an id was loaded, false otherwise.
        /// </summary>
        bool TryLoadSelectedModelId(out string? id);

        /// <summary>
        /// Save the selected model id. Implementations should swallow exceptions.
        /// </summary>
        void SaveSelectedModelId(string id);

        /// <summary>
        /// Clear any saved state. Optional.
        /// </summary>
        void Clear();
    }
}
