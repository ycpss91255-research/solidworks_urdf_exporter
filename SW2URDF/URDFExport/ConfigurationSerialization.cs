using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.Legacy;
using SW2URDF.URDF;
using SW2URDF.UI;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace SW2URDF.URDFExport
{
    /// <summary>
    /// Class to serialize URDF trees to string so they can be saved to an SW Attribute in the
    /// top-level assembly document.
    ///
    /// Any changes to the serialization scheme need to support backwards compatibility in some way.
    /// At least in regards to reading the old configuration. I'm also choosing to save any old xml
    /// formats to a second attribute in case they need to be reloaded.
    /// </summary>
    public static class ConfigurationSerialization
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        /// <summary>
        /// Current Serialization version
        /// </summary>
        private const double SerializationVersion = ConfigurationFormat.CurrentVersion;

        /// <summary>
        /// The name given to the URDF configuration in the ModelDoc Feature tree. This is displayed to the
        /// user
        /// </summary>
        public const string UrdfConfigurationSwAttributeName = ConfigurationFormat.SharedAttributeName;

        /// <summary>
        /// Definition name of the per-SolidWorks-configuration attribute. One instance is stored per
        /// SolidWorks configuration and is named after that configuration. The serialization format
        /// is the same as the shared attribute (<see cref="SerializationVersion"/>), only the
        /// storage location differs. Note: if a SolidWorks configuration is renamed its attribute no
        /// longer matches and the exporter falls back to the shared attribute until the
        /// configuration is saved again (the stale attribute is harmless and can be deleted).
        /// </summary>
        private const string ConfigurationAttributeDefinitionName = "URDF Export Configuration Data";

        /// <summary>
        /// Feature tree folder that groups the per-configuration attributes. It cannot contain a
        /// '.' because SolidWorks rejects such folder names, so it carries no version.
        /// </summary>
        public const string ConfigurationAttributeFolderName = "URDF Export Configuration";

        public static List<string> PREVIOUS_URDF_CONFIGURATION_NAMES =
            new List<string>(ConfigurationFormat.PreviousSharedAttributeNames);

        #region Public Methods

        /// <summary>
        /// Loads the URDF tree from the SW Model Document
        /// </summary>
        /// <param name="model">ModelDoc containing the URDF configuration</param>
        /// <returns>TreeView LinkNode loaded from configuration</returns>
        public static LinkNode LoadBaseNodeFromModel(ModelDoc2 model, out bool error, UiText ui = null)
        {
            ui = ui ?? new UiText(null);
            LogOrphanConfigurationAttributes(model);
            string data = GetConfigTreeData(model, out double configVersion);

            LinkNode basenode;
            ConfigurationFormatKind kind = ConfigurationFormat.Classify(configVersion);
            if (kind == ConfigurationFormatKind.TooNew)
            {
                MessageBox.Show(string.Format(ui.Get("The configuration saved in this model is newer than what this exporter supports ({0} > {1}). Please update your exporter version"), configVersion, SerializationVersion));
                error = true;
                return null;
            }

            if (kind == ConfigurationFormatKind.DataContract)
            {
                basenode = DeserializeFromString(data);
            }
            else
            {
                basenode = LoadConfigFromStringXML(data);
            }

            error = false;
            return basenode;
        }

        /// <summary>
        /// Public method to serialize a Treeview LinkNode URDF data to a string and saves it to a SW ModelDoc
        /// </summary>
        /// <param name="swApp">SldWorks application</param>
        /// <param name="model">ModelDoc to which you are saving this data</param>
        /// <param name="BaseNode">TreeView LinkNode which contains the data you are saving</param>
        /// <param name="warnUser">Warn the user with a YesNo MessageBox, otherwise this will be done silently
        ///  and overwrite any existing data</param>
        public static void SaveConfigTreeXML(SldWorks swApp, ModelDoc2 model, LinkNode BaseNode, bool warnUser, UiText ui = null)
        {
            ui = ui ?? new UiText(null);
            string oldData = GetConfigTreeData(model, out double version);
            if (oldData.Length > 0 && ConfigurationFormat.NeedsUpgrade(version))
            {
                MessageBox.Show(ui.Get("You have a URDF configuration with an outdated save format. It will automatically be upgraded to the latest version and saved for the active SolidWorks configuration. The old data is left in place and can be deleted at your convenience."));
                warnUser = false;
            }

            string newData = SerializeToString(BaseNode);
            if (BaseNode != null && string.IsNullOrEmpty(newData))
            {
                MessageBox.Show(ui.Get("Serializing this link failed. Please email your maintainer with your SW assembly."));
                return;
            }
            if (oldData != newData || !HasConfigurationAttribute(model))
            {
                if (!warnUser ||
                    (warnUser &&
                    MessageBox.Show(ui.Get("The configuration has changed, would you like to save?"),
                    ui.Get("Save Export Configuration"), MessageBoxButtons.YesNo) == DialogResult.Yes))
                {
                    if (SaveDataToModelDoc(swApp, model, newData, ui))
                    {
                        PersistDocument(model, ui);
                    }
                }
            }
        }

        /// <summary>
        /// Makes sure the active SolidWorks configuration has its own URDF attribute (visible under
        /// the "URDF Export Configuration" feature tree folder, named like the configuration). Other
        /// configurations get theirs when the user switches to them and opens the exporter. A
        /// configuration without one gets a copy of the shared (legacy) data, or a blank attribute
        /// when there is none. Existing attributes are never changed. A missing folder is rebuilt
        /// around the attributes that exist. Returns true when anything was created or rebuilt
        /// (the document then needs saving).
        /// </summary>
        public static bool EnsureConfigurationAttributes(SldWorks swApp, ModelDoc2 model)
        {
            string[] names = model.GetConfigurationNames() as string[];
            string active = GetActiveConfigurationName(model);
            List<string> existing = new List<string>();
            if (names != null)
            {
                foreach (string name in names)
                {
                    if (FindConfigurationAttributeFeature(model, name) != null)
                    {
                        existing.Add(name);
                    }
                }
            }

            bool created = false;
            string[] toCreate = DocumentPersistence.NamesToCreate(names, active, existing.ToArray());
            if (toCreate.Length > 0)
            {
                SolidWorks.Interop.sldworks.Attribute shared =
                    FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName) ?? CheckForOldAttributes(model);
                string sharedData = shared == null ? "" : shared.GetParameter("data").GetStringValue();
                double sharedVersion = shared == null ? SerializationVersion : shared.GetParameter("exporterVersion").GetDoubleValue();
                AttributeInit init = DocumentPersistence.DecideAttribute(false, !string.IsNullOrEmpty(sharedData));

                SolidWorks.Interop.sldworks.Attribute att = CreateConfigurationAttribute(swApp, model, active);
                if (att == null)
                {
                    logger.Warn("Could not create the URDF attribute for configuration \"" + active + "\"");
                }
                else
                {
                    int options = (int)swInConfigurationOpts_e.swThisConfiguration;
                    att.GetParameter("data").SetStringValue2(init == AttributeInit.CopyShared ? sharedData : "", options, "");
                    att.GetParameter("name").SetStringValue2("config1", options, "");
                    att.GetParameter("date").SetStringValue2(DateTime.Now.ToString(), options, "");
                    att.GetParameter("exporterVersion").SetDoubleValue2(
                        init == AttributeInit.CopyShared ? sharedVersion : SerializationVersion, options, "");
                    created = true;
                }
            }

            bool folderExisted = ConfigurationFolderExists(model);
            if (DocumentPersistence.NeedsGrouping(created, folderExisted))
            {
                TryGroupConfigurationAttributes(model);
                // Rebuilding a deleted folder changes the document too
                created = created || (!folderExisted && ConfigurationFolderExists(model));
            }
            return created;
        }
        /// <summary>
        /// Where the settings for the active SolidWorks configuration come from (what
        /// <see cref="LoadBaseNodeFromModel"/> loads), without loading them.
        /// </summary>
        public static SettingsSource GetSettingsSource(ModelDoc2 model)
        {
            bool hasOwn = false;
            bool hasShared = false;
            try
            {
                Feature configFeature = FindConfigurationAttributeFeature(model, GetActiveConfigurationName(model));
                if (configFeature != null)
                {
                    SolidWorks.Interop.sldworks.Attribute att =
                        (SolidWorks.Interop.sldworks.Attribute)configFeature.GetSpecificFeature2();
                    hasOwn = !string.IsNullOrEmpty(att.GetParameter("data").GetStringValue());
                }
                if (!hasOwn)
                {
                    SolidWorks.Interop.sldworks.Attribute shared =
                        FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName) ?? CheckForOldAttributes(model);
                    hasShared = shared != null && !string.IsNullOrEmpty(shared.GetParameter("data").GetStringValue());
                }
            }
            catch (Exception e)
            {
                logger.Warn("Could not determine where the URDF settings come from", e);
            }
            return ConfigurationStatus.Decide(hasOwn, hasShared);
        }

        /// <summary>Localized one-line status: active configuration and where its settings come from.</summary>
        public static string DescribeSettings(ModelDoc2 model, UiText ui)
        {
            return ConfigurationStatus.Describe(
                ui, GetActiveConfigurationName(model), GetSettingsSource(model));
        }

        #endregion Public Methods

        #region Private Methods

        /// <summary>
        /// If someone updates the name of a LinkNode in the Treeview, it needs to be pushed down
        /// to the URDF Link itself.
        /// </summary>
        /// <param name="node">TreeView LinkNode to save properties of to its URDF Link</param>
        private static void SavePropertiesLinkNodeToLink(LinkNode node)
        {
            if (node.Link == null)
            {
                node.Link = new Link();
                return;
            }

            node.Link.Name = node.Name;

            foreach (LinkNode child in node.Nodes)
            {
                SavePropertiesLinkNodeToLink(child);
            }
        }

        /// <summary>
        /// Data Contract serialization. All members of an object need to be annotated with a
        /// [DataMember] attribute.
        /// </summary>
        /// <param name="node">TreeView LinkNode to serialize</param>
        /// <returns>A string serialized utilizing DataContract serialization XML scheme</returns>
        private static string SerializeToString(LinkNode node)
        {
            SavePropertiesLinkNodeToLink(node);
            Link link = node.UpdateLinkTree(null);
            string data = "";
            using (MemoryStream stream = new MemoryStream())
            {
                DataContractSerializer ser =
                    new DataContractSerializer(typeof(Link));

                try
                {
                    ser.WriteObject(stream, link);
                    stream.Flush();
                    data = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Position);
                }
                catch (SerializationException e)
                {
                    logger.Error("Serialization failed with exception, returning empty string", e);
                }
            }
            return data;
        }

        /// <summary>
        /// Read a URDF Link from a serialized string
        /// </summary>
        /// <param name="data">Data string to read into a TreeView LinkNode</param>
        /// <returns>Deserialized LinkNode</returns>
        private static LinkNode DeserializeFromString(string data)
        {
            LinkNode baseNode = null;
            if (!string.IsNullOrWhiteSpace(data))
            {
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
                {
                    DataContractSerializer ser =
                        new DataContractSerializer(typeof(Link));

                    try
                    {
                        Link link = (Link)ser.ReadObject(stream);

                        // By copying this link, we can ensure that all non-serialized properties are setup correctly
                        Link copy = link.Clone();
                        baseNode = new LinkNode(copy);
                    }
                    catch (SerializationException e)
                    {
                        logger.Error("Deserialization failed with exception, returning empty LinkNode", e);
                        logger.Error(data);
                    }
                }
            }
            return baseNode;
        }

        /// <summary>
        /// Load from the deprecated XML serialized scheme
        /// </summary>
        /// <param name="data">Data string to deserialize using XMLSerializer</param>
        /// <returns>TreeView LinkNode</returns>
        private static LinkNode LoadConfigFromStringXML(string data)
        {
            LinkNode baseNode = null;
            if (!string.IsNullOrWhiteSpace(data))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(SerialNode));
                XmlTextReader textReader = new XmlTextReader(new StringReader(data));
                // Not reading external files, so this can set to prohibit. Resolves CA3075
                textReader.DtdProcessing = DtdProcessing.Prohibit;
                SerialNode sNode = (SerialNode)serializer.Deserialize(textReader);
                textReader.Close();

                baseNode = sNode.BuildLinkNodeFromSerialNode();
            }
            return baseNode;
        }

        /// <summary>
        /// For the future, if serialization is upgraded again, this might look for several versions
        /// </summary>
        /// <param name="model">ModelDoc to look through</param>
        /// <returns>SolidWorks Attribute with older serialization schemes if found, otherwise null.</returns>
        private static SolidWorks.Interop.sldworks.Attribute CheckForOldAttributes(ModelDoc2 model)
        {
            foreach (string configurationName in PREVIOUS_URDF_CONFIGURATION_NAMES)
            {
                SolidWorks.Interop.sldworks.Attribute swAtt = FindSWSaveAttribute(model, configurationName);
                if (swAtt != null)
                {
                    return swAtt;
                }
            }
            return null;
        }

        /// <summary>
        /// Find the SW attribute that contains the URDF configuration serialized string
        /// </summary>
        /// <param name="model">ModelDoc model to load URDF configuration from</param>
        /// <param name="version">Output parameter of the serialization version</param>
        /// <returns>Serialized data string</returns>
        private static string GetConfigTreeData(ModelDoc2 model, out double version)
        {
            string data = "";
            version = 0.0;

            // Prefer the attribute stored for the active SolidWorks configuration
            Feature configFeature = FindConfigurationAttributeFeature(model, GetActiveConfigurationName(model));
            if (configFeature != null)
            {
                SolidWorks.Interop.sldworks.Attribute configAtt =
                    (SolidWorks.Interop.sldworks.Attribute)configFeature.GetSpecificFeature2();
                string configData = configAtt.GetParameter("data").GetStringValue();
                if (!string.IsNullOrEmpty(configData))
                {
                    logger.Info("URDF Configuration found for the active SolidWorks configuration\n" + configData);
                    version = configAtt.GetParameter("exporterVersion").GetDoubleValue();
                    return configData;
                }
            }

            // Otherwise fall back to the shared attribute, so older models keep working unchanged.
            // Check for most recent serialization version
            SolidWorks.Interop.sldworks.Attribute swAtt =
                FindSWSaveAttribute(model, UrdfConfigurationSwAttributeName);

            // If not found, check for an older version
            if (swAtt == null)
            {
                swAtt = CheckForOldAttributes(model);
            }

            if (swAtt != null)
            {
                Parameter param = swAtt.GetParameter("data");
                data = param.GetStringValue();
                logger.Info("URDF Configuration found\n" + data);

                param = swAtt.GetParameter("exporterVersion");
                version = param.GetDoubleValue();
            }

            return data;
        }

        /// <summary>
        ///  Iterates through features in ModelDoc to find a feature of the correct name
        /// </summary>
        /// <param name="model">ModelDoc of features to iterate through</param>
        /// <param name="featName">The name of the feature to get</param>
        /// <returns>The SolidWorks Feature if found, null otherwise</returns>
        private static Feature GetFeatureAttributeByName(ModelDoc2 model, string featName)
        {
            Object[] objects = model.FeatureManager.GetFeatures(true);
            foreach (Object obj in objects)
            {
                Feature feature = (Feature)obj;
                if (feature.GetTypeName2() == "Attribute")
                {
                    SolidWorks.Interop.sldworks.Attribute att =
                        (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                    if (att.GetName() == featName)
                    {
                        return feature;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Finds existing SWSave Attribute in a ModelDoc
        /// </summary>
        /// <param name="model">ModelDoc model to search through</param>
        /// <param name="name">Name of attribute to find</param>
        /// <returns>SolidWorks Attribute if found, otherwise null</returns>
        private static SolidWorks.Interop.sldworks.Attribute
            FindSWSaveAttribute(ModelDoc2 model, string name)
        {
            Feature feature = GetFeatureAttributeByName(model, name);

            if (feature == null)
            {
                return null;
            }
            return (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
        }

        /// <summary>
        /// Detection only (issue #11): logs per-configuration attributes that match no
        /// configuration. Nothing is changed and loading is never blocked.
        /// </summary>
        private static void LogOrphanConfigurationAttributes(ModelDoc2 model)
        {
            try
            {
                List<string> attributeNames = new List<string>();
                foreach (Feature feature in GetConfigurationAttributeFeatures(model))
                {
                    SolidWorks.Interop.sldworks.Attribute att =
                        (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                    // Instances of the shared attribute definitions also carry data/exporterVersion
                    if (att.GetName() != null && !IsSharedAttributeName(att.GetName()))
                    {
                        attributeNames.Add(att.GetName());
                    }
                }
                string[] configNames = model.GetConfigurationNames() as string[];
                List<string> orphans = OrphanAttributes.FindOrphans(attributeNames, configNames);
                string message = OrphanAttributes.BuildLogMessage(
                    orphans, OrphanAttributes.FindConfigurationsWithoutAttribute(attributeNames, configNames));
                if (message != null)
                {
                    logger.Warn(message);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Checking for orphan URDF configuration attributes failed", e);
            }
        }

        private static bool IsSharedAttributeName(string name)
        {
            return name == ConfigurationFormat.SharedAttributeName ||
                ConfigurationFormat.PreviousSharedAttributeNames.Contains(name);
        }

        public static string GetActiveConfigurationName(ModelDoc2 model)
        {
            try
            {
                Configuration configuration = model.ConfigurationManager?.ActiveConfiguration;
                string name = configuration?.Name;
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception e)
            {
                logger.Warn("Could not read the active configuration name", e);
                return null;
            }
        }

        /// <summary>
        /// Walks the whole feature chain (which also reaches features inside tree folders) and
        /// returns the per-configuration attribute features: Attribute features that have both a
        /// "data" and an "exporterVersion" parameter.
        /// </summary>
        private static List<Feature> GetConfigurationAttributeFeatures(ModelDoc2 model)
        {
            List<Feature> result = new List<Feature>();
            Feature feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                if (feature.GetTypeName2() == "Attribute")
                {
                    SolidWorks.Interop.sldworks.Attribute att =
                        feature.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;
                    if (att != null && att.GetParameter("data") != null &&
                        att.GetParameter("exporterVersion") != null)
                    {
                        result.Add(feature);
                    }
                }
                feature = feature.GetNextFeature() as Feature;
            }
            return result;
        }

        private static Feature FindConfigurationAttributeFeature(ModelDoc2 model, string configurationName)
        {
            if (string.IsNullOrEmpty(configurationName))
            {
                return null;
            }
            foreach (Feature feature in GetConfigurationAttributeFeatures(model))
            {
                SolidWorks.Interop.sldworks.Attribute att =
                    (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                if (att.GetName() == configurationName)
                {
                    return feature;
                }
            }
            return null;
        }

        private static bool ConfigurationFolderExists(ModelDoc2 model)
        {
            Feature feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                if (feature.GetTypeName2() == "FtrFolder" && feature.Name == ConfigurationAttributeFolderName)
                {
                    return true;
                }
                feature = feature.GetNextFeature() as Feature;
            }
            return false;
        }

        private static bool HasConfigurationAttribute(ModelDoc2 model)
        {
            string name = GetActiveConfigurationName(model);
            return name != null && FindConfigurationAttributeFeature(model, name) != null;
        }

        /// <summary>
        /// Appearance only: put all per-configuration attributes in one feature tree folder.
        /// SolidWorks cannot move a feature into an existing folder, so the old folder is deleted
        /// (deleting a folder keeps its contents) and recreated around all attributes. Failures are
        /// logged and never interrupt saving.
        /// </summary>
        private static void TryGroupConfigurationAttributes(ModelDoc2 model)
        {
            try
            {
                string[] configNames = (string[])model.GetConfigurationNames();
                List<Feature> attributes = new List<Feature>();
                foreach (Feature feature in GetConfigurationAttributeFeatures(model))
                {
                    SolidWorks.Interop.sldworks.Attribute att =
                        (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                    if (Array.IndexOf(configNames, att.GetName()) >= 0)
                    {
                        attributes.Add(feature);
                    }
                }
                if (attributes.Count == 0)
                {
                    return;
                }

                model.ClearSelection2(true);
                Feature folder = model.FirstFeature() as Feature;
                while (folder != null)
                {
                    if (folder.GetTypeName2() == "FtrFolder" && folder.Name == ConfigurationAttributeFolderName)
                    {
                        folder.Select2(false, 0);
                        // 0 = delete only the folder, its contents are kept
                        model.Extension.DeleteSelection2(0);
                        break;
                    }
                    folder = folder.GetNextFeature() as Feature;
                }

                model.ClearSelection2(true);
                bool append = false;
                foreach (Feature feature in attributes)
                {
                    feature.Select2(append, 0);
                    append = true;
                }
                Feature newFolder = model.FeatureManager.InsertFeatureTreeFolder2(
                    (int)swFeatureTreeFolderType_e.swFeatureTreeFolder_Containing) as Feature;
                if (newFolder != null)
                {
                    newFolder.Name = ConfigurationAttributeFolderName;
                }
                else
                {
                    logger.Warn("Could not create the URDF configuration feature tree folder");
                }
                model.ClearSelection2(true);
            }
            catch (Exception e)
            {
                logger.Warn("Grouping the URDF configuration attributes into a folder failed", e);
            }
        }

        /// <summary>
        /// Builds a SW Attribute for saving our serialized data
        /// </summary>
        /// <param name="swApp">SolidWorks Application to build Feature Definition</param>
        /// <param name="model">ModelDoc in which this attribute will be saved</param>
        /// <param name="name">Name of the attribute to create</param>
        /// <returns>Constructed SolidWorks Attribute</returns>
        private static SolidWorks.Interop.sldworks.Attribute
            CreateSWSaveAttribute(SldWorks swApp, ModelDoc2 model, string name)
        {
            SolidWorks.Interop.sldworks.Attribute existingAttribute =
                FindSWSaveAttribute(model, name);
            if (existingAttribute != null)
            {
                return existingAttribute;
            }

            int ConfigurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;

            int Options = 0;
            AttributeDef saveConfigurationAttributeDef;
            saveConfigurationAttributeDef = swApp.DefineAttribute(UrdfConfigurationSwAttributeName);

            saveConfigurationAttributeDef.AddParameter(
                "data", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "name", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "date", (int)swParamType_e.swParamTypeString, 0, Options);
            saveConfigurationAttributeDef.AddParameter(
                "exporterVersion", (int)swParamType_e.swParamTypeDouble, SerializationVersion, Options);
            saveConfigurationAttributeDef.Register();

            SolidWorks.Interop.sldworks.Attribute saveExporterAttribute =
                saveConfigurationAttributeDef.CreateInstance5(
                    model, null, UrdfConfigurationSwAttributeName, Options, ConfigurationOptions);
            return saveExporterAttribute;
        }

        /// <summary>
        /// Saves a string of data to the SWModelDoc
        /// </summary>
        /// <param name="swApp">SolidWorks Application</param>
        /// <param name="model">ModelDoc model to save data string to</param>
        /// <param name="data">string to save</param>
        /// <param name="attributeName">Name of attribute to save to</param>
        private static bool SaveDataToModelDoc(SldWorks swApp, ModelDoc2 model,
            string data, UiText ui)
        {
            string configurationName = GetActiveConfigurationName(model);
            int ConfigurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;
            bool created = false;
            SolidWorks.Interop.sldworks.Attribute saveExporterAttribute;
            if (configurationName == null)
            {
                // No usable active configuration: keep the shared attribute behavior
                saveExporterAttribute =
                    CreateSWSaveAttribute(swApp, model, UrdfConfigurationSwAttributeName);
            }
            else
            {
                ConfigurationOptions = (int)swInConfigurationOpts_e.swThisConfiguration;
                Feature existing = FindConfigurationAttributeFeature(model, configurationName);
                if (existing != null)
                {
                    saveExporterAttribute =
                        (SolidWorks.Interop.sldworks.Attribute)existing.GetSpecificFeature2();
                }
                else
                {
                    saveExporterAttribute = CreateConfigurationAttribute(swApp, model, configurationName);
                    created = true;
                }
            }

            if (saveExporterAttribute == null)
            {
                logger.Error("SolidWorks did not create the URDF configuration attribute" +
                    (configurationName == null ? "" : " for configuration \"" + configurationName + "\""));
                MessageBox.Show(ui.Get("The URDF configuration could not be saved to this model."));
                return false;
            }

            Parameter param = saveExporterAttribute.GetParameter("data");
            param.SetStringValue2(data, ConfigurationOptions, "");
            param = saveExporterAttribute.GetParameter("name");
            param.SetStringValue2("config1", ConfigurationOptions, "");
            param = saveExporterAttribute.GetParameter("date");
            param.SetStringValue2(DateTime.Now.ToString(), ConfigurationOptions, "");
            param = saveExporterAttribute.GetParameter("exporterVersion");
            param.SetDoubleValue2(SerializationVersion, ConfigurationOptions, "");

            if (DocumentPersistence.NeedsGrouping(created, ConfigurationFolderExists(model)))
            {
                TryGroupConfigurationAttributes(model);
            }
            return true;
        }

        /// <summary>
        /// The settings live in the open document: without saving it they are lost as soon as the
        /// document is closed. Saves a document that has a file (silently, only this document, not
        /// its references); otherwise tells the user to save it.
        /// </summary>
        public static void PersistDocument(ModelDoc2 model, UiText ui)
        {
            bool saved = false;
            PersistAction action = DocumentPersistence.Decide(
                true, !string.IsNullOrEmpty(model.GetPathName()), model.IsOpenedReadOnly());
            if (action == PersistAction.Save)
            {
                int errors = 0;
                int warnings = 0;
                try
                {
                    saved = model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
                }
                catch (Exception e)
                {
                    logger.Warn("Saving the document after storing the URDF settings failed", e);
                }
                logger.Info("Saved the document after storing the URDF settings: " + saved + " (errors " + errors + ", warnings " + warnings + ")");
            }
            if (action == PersistAction.WarnNotSaved || (action == PersistAction.Save && !saved))
            {
                MessageBox.Show(DocumentPersistence.WarningText(ui), DocumentPersistence.WarningTitle(ui));
            }
        }

        private static SolidWorks.Interop.sldworks.Attribute
            CreateConfigurationAttribute(SldWorks swApp, ModelDoc2 model, string configurationName)
        {
            int Options = 0;
            AttributeDef attributeDef = swApp.DefineAttribute(ConfigurationAttributeDefinitionName);
            attributeDef.AddParameter(
                "data", (int)swParamType_e.swParamTypeString, 0, Options);
            attributeDef.AddParameter(
                "name", (int)swParamType_e.swParamTypeString, 0, Options);
            attributeDef.AddParameter(
                "date", (int)swParamType_e.swParamTypeString, 0, Options);
            attributeDef.AddParameter(
                "exporterVersion", (int)swParamType_e.swParamTypeDouble, SerializationVersion, Options);
            attributeDef.Register();

            return attributeDef.CreateInstance5(
                model, null, configurationName, Options,
                (int)swInConfigurationOpts_e.swThisConfiguration);
        }

        #endregion Private Methods
    }
}