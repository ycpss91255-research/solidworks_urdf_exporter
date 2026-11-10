using SW2URDF.URDF;
using SW2URDF.URDFExport;
using System.Reflection;
using Xunit;

namespace SW2URDF.Test
{
    /// <summary>
    /// Protects the data format version rules. Does not need SolidWorks, so it has no fixture.
    /// </summary>
    public class TestConfigurationFormat
    {
        private static object InvokeSerialization(string method, object argument)
        {
            return typeof(ConfigurationSerialization)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new[] { argument });
        }

        [Fact]
        public void CurrentVersionIs1Point4AndSharedNameIsUnchanged()
        {
            // Renaming the shared attribute would hide it from models saved by older exporters.
            Assert.Equal(1.4, ConfigurationFormat.CurrentVersion);
            Assert.Equal("URDF Export Configuration (v1.4)", ConfigurationFormat.SharedAttributeName);
            Assert.Equal(ConfigurationFormat.SharedAttributeName,
                ConfigurationSerialization.UrdfConfigurationSwAttributeName);
        }

        [Fact]
        public void OlderSharedAttributeNamesAreStillRead()
        {
            Assert.Contains("URDF Export Configuration (v1.3)", ConfigurationFormat.PreviousSharedAttributeNames);
            Assert.Contains("URDF Export Configuration", ConfigurationFormat.PreviousSharedAttributeNames);
        }

        [Fact]
        public void FolderNameCarriesNoVersion()
        {
            Assert.Equal("URDF Export Configuration", ConfigurationSerialization.ConfigurationAttributeFolderName);
        }

        [Fact]
        public void VersionsAreClassified()
        {
            Assert.Equal(ConfigurationFormatKind.None, ConfigurationFormat.Classify(0.0));
            Assert.Equal(ConfigurationFormatKind.LegacyXml, ConfigurationFormat.Classify(1.0));
            Assert.Equal(ConfigurationFormatKind.LegacyXml, ConfigurationFormat.Classify(1.29));
            Assert.Equal(ConfigurationFormatKind.DataContract, ConfigurationFormat.Classify(1.3));
            Assert.Equal(ConfigurationFormatKind.DataContract, ConfigurationFormat.Classify(1.4));
            Assert.Equal(ConfigurationFormatKind.TooNew, ConfigurationFormat.Classify(1.5));
            Assert.Equal(ConfigurationFormatKind.TooNew, ConfigurationFormat.Classify(2.0));
        }

        [Fact]
        public void OnlyOlderVersionsNeedUpgrade()
        {
            Assert.True(ConfigurationFormat.NeedsUpgrade(1.0));
            Assert.True(ConfigurationFormat.NeedsUpgrade(1.3));
            Assert.False(ConfigurationFormat.NeedsUpgrade(1.4));
            Assert.False(ConfigurationFormat.NeedsUpgrade(1.5));
        }

        [Fact]
        public void LoadingAndResavingKeepsContentUnchanged()
        {
            Link root = new Link();
            root.Name = "base_link";
            Link child = new Link();
            child.Name = "arm_link";
            child.Joint.Name = "arm_joint";
            root.Children.Add(child);
            LinkNode node = new LinkNode(root);

            string saved = (string)InvokeSerialization("SerializeToString", node);
            Assert.False(string.IsNullOrEmpty(saved));

            LinkNode loaded = (LinkNode)InvokeSerialization("DeserializeFromString", saved);
            Assert.NotNull(loaded);
            Assert.Single(loaded.Nodes);
            Assert.Equal("arm_joint", ((LinkNode)loaded.Nodes[0]).Link.Joint.Name);

            string resaved = (string)InvokeSerialization("SerializeToString", loaded);
            Assert.Equal(saved, resaved);
        }
    }
}
