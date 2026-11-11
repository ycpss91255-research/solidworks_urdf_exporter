using System;

namespace SW2URDF.URDFExport
{
    /// <summary>Serialized settings of one attribute and the format version stored with it.</summary>
    public sealed class AttributeData
    {
        public AttributeData(string data, double version)
        {
            Data = data;
            Version = version;
        }

        public string Data { get; private set; }
        public double Version { get; private set; }
    }

    /// <summary>
    /// What the cleanup needs from a model. SolidWorks implements it in
    /// <see cref="ConfigurationSerialization"/>; tests use a fake document.
    /// </summary>
    public interface ISharedAttributeDocument
    {
        string[] ConfigurationNames();

        /// <summary>True when the configuration has an entry (cheap, does not read its data).</summary>
        bool HasEntry(string configuration);

        /// <summary>The entry of a SolidWorks configuration, or null when it has none. May be slow for a configuration that is not active.</summary>
        AttributeData GetEntry(string configuration);

        /// <summary>
        /// The shared attribute the loader falls back to (the current one, else the first older
        /// one), or null when the model has none.
        /// </summary>
        AttributeData GetSharedFallback();

        /// <summary>Deletes every shared attribute. True when they are gone afterwards.</summary>
        bool DeleteSharedAttributes();
    }

    public enum SharedCleanupResult
    {
        NothingToRemove,
        Removed,
        KeptMissingEntry,
        KeptDiffers,
        KeptUnparsable,
        KeptDeleteFailed
    }

    /// <summary>
    /// The old shared attributes (all configurations) are only a read fallback for a configuration
    /// that has no entry of its own. Once every configuration has its own entry holding the same
    /// link/joint data, they are deleted. Anything doubtful keeps everything. Free of SolidWorks
    /// types so it can be unit tested.
    /// </summary>
    public static class SharedAttributeCleanup
    {
        /// <param name="canonical">
        /// Deserializes settings of a given version and returns a canonical form of the resulting
        /// tree, or null when they cannot be read. Trees are compared through it, never the raw
        /// strings (they may be in different formats).
        /// </param>
        /// <param name="log">Receives the reason when something is kept.</param>
        public static SharedCleanupResult Run(
            ISharedAttributeDocument document, Func<string, double, string> canonical, Action<string> log)
        {
            AttributeData shared = document.GetSharedFallback();
            if (shared == null)
            {
                return SharedCleanupResult.NothingToRemove;
            }

            string[] configurations = document.ConfigurationNames() ?? new string[0];
            if (configurations.Length == 0)
            {
                log("Shared URDF attributes kept: the model reports no configuration");
                return SharedCleanupResult.KeptMissingEntry;
            }
            foreach (string name in configurations)
            {
                if (!document.HasEntry(name))
                {
                    log("Shared URDF attributes kept: configuration \"" + name + "\" has no entry yet");
                    return SharedCleanupResult.KeptMissingEntry;
                }
            }

            string expected = string.IsNullOrWhiteSpace(shared.Data) ? "" : canonical(shared.Data, shared.Version);
            if (expected == null)
            {
                log("Shared URDF attributes kept: they cannot be read");
                return SharedCleanupResult.KeptUnparsable;
            }
            foreach (string name in configurations)
            {
                AttributeData entry = document.GetEntry(name);
                // An entry without data is read through the shared attribute, so it is still needed
                if (entry == null || string.IsNullOrEmpty(entry.Data))
                {
                    log("Shared URDF attributes kept: the entry of configuration \"" + name + "\" holds no data yet");
                    return SharedCleanupResult.KeptMissingEntry;
                }
                string actual = canonical(entry.Data, entry.Version);
                if (actual == null)
                {
                    log("Shared URDF attributes kept: the entry of configuration \"" + name + "\" cannot be read");
                    return SharedCleanupResult.KeptUnparsable;
                }
                if (actual != expected)
                {
                    log("Shared URDF attributes kept: the entry of configuration \"" + name + "\" differs from them");
                    return SharedCleanupResult.KeptDiffers;
                }
            }

            if (!document.DeleteSharedAttributes())
            {
                log("Shared URDF attributes kept: SolidWorks could not delete them");
                return SharedCleanupResult.KeptDeleteFailed;
            }
            log("Removed the old shared URDF attributes: every configuration has its own entry with the same settings");
            return SharedCleanupResult.Removed;
        }
    }
}
