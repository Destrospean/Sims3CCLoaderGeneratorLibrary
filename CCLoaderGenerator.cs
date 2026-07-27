using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using Mono.Cecil;
using s3pi.Interfaces;

namespace Destrospean.CCLoaderGeneratorLibrary
{
    public class ResourceKey : IResourceKey
    {
        public ulong Instance
        {
            get;
            set;
        }

        public uint ResourceGroup
        {
            get;
            set;
        }

        public uint ResourceType
        {
            get;
            set;
        }
            
        public ResourceKey(uint type, uint group, ulong instance)
        {
            Instance = instance;
            ResourceGroup = group;
            ResourceType = type;
        }

        public int CompareTo(IResourceKey other)
        {
            var result = ResourceType.CompareTo(other.ResourceType);
            if (result != 0 || (result = ResourceGroup.CompareTo(other.ResourceGroup)) != 0)
            {
                return result;
            }
            return Instance.CompareTo(other.Instance);
        }

        public bool Equals(IResourceKey a, IResourceKey b)
        {
            return a.Equals(b);
        }

        public bool Equals(IResourceKey other)
        {
            return CompareTo(other) == 0;
        }

        public override int GetHashCode()
        {
            return ResourceType.GetHashCode() ^ ResourceGroup.GetHashCode() ^ Instance.GetHashCode();
        }

        public int GetHashCode(IResourceKey resourceKey)
        {
            return resourceKey.GetHashCode();
        }
    }

    public class CCLoaderGenerator
    {
        const string kResourcePathPrefix = "Destrospean.CCLoaderGeneratorLibrary.base._";

        readonly Dictionary<string, XmlDocument> mXmlDocuments = new Dictionary<string, XmlDocument>();

        public readonly string AssemblyName;

        public readonly IPackage Package;

        [Flags]
        public enum XmlTypes
        {
            All = 127,
            Books = 1,
            Buffs,
            Data = 4,
            EventHandlers = 8,
            Ingredients = 16,
            Plants = 32,
            Recipes = 64
        }

        public CCLoaderGenerator(string assemblyName, IPackage package)
        {
            AssemblyName = assemblyName;
            Package = package;
            PopulateXmlDocuments(System.Reflection.Assembly.GetCallingAssembly());
        }

        void PopulateXmlDocuments(System.Reflection.Assembly assembly) 
        {
            foreach (var resourceName in Array.FindAll(assembly.GetManifestResourceNames(), x => x.StartsWith(kResourcePathPrefix) && x.EndsWith("._xml")))
            {
                using (var reader = new StreamReader(assembly.GetManifestResourceStream(resourceName)))
                {
                    var xmlDocument = new XmlDocument();
                    xmlDocument.LoadXml(reader.ReadToEnd());
                    mXmlDocuments.Add(resourceName, xmlDocument);
                }
            }
        }

        public void AddDataEntry(string name = "", string creator = "", XmlTypes xmlTypes = XmlTypes.All)
        {
            var xmlDocument = GetResourceAsXmlDocument(XmlTypes.Data);
            XmlNode root = xmlDocument.SelectSingleNode("CCLoader"),
            clonedNode = root.ChildNodes[1].CloneNode(true);
            foreach (XmlNode childNode in clonedNode.ChildNodes)
            {
                switch (childNode.Name)
                {
                    case "Name":
                        childNode.InnerText = name;
                        break;
                    case "Creator":
                        childNode.InnerText = creator;
                        break;
                    case "Books_XML":
                        childNode.InnerText = (xmlTypes & XmlTypes.Books) == 0 ? "" : AssemblyName + "_Books.xml";
                        break;
                    case "Buffs_XML":
                        childNode.InnerText = (xmlTypes & XmlTypes.Buffs) == 0 ? "" : AssemblyName + "_Buffs.xml";
                        break;
                    case "EventHandlers_XML":
                        childNode.InnerText = (xmlTypes & XmlTypes.EventHandlers) == 0 ? "" : AssemblyName + "_EventHandlers.xml";
                        break;
                    case "Ingredients_XML":
                        childNode.InnerText = (xmlTypes & XmlTypes.Ingredients) == 0 ? "" : AssemblyName + "_Ingredients.xml";
                        break;
                    case "Plants_XML":
                        childNode.InnerText = (xmlTypes & XmlTypes.Plants) == 0 ? "" : AssemblyName + "_Plants.xml";
                        break;
                    case "Recipes_XML":
                        childNode.InnerText = (xmlTypes & XmlTypes.Recipes) == 0 ? "" : AssemblyName + "_Recipes.xml";
                        break;
                }
            }
            root.AppendChild(clonedNode);
            var resourceIndexEntry = GetResourceIndexEntry(XmlTypes.Data);
            Package.DeleteResource(resourceIndexEntry);
            var xmlStream = new MemoryStream();
            xmlDocument.Save(xmlStream);
            Package.AddResource(resourceIndexEntry, xmlStream, true);
        }

        public void AddDataEntry(XmlTypes xmlTypes)
        {
            AddDataEntry("", "", xmlTypes);
        }

        public void AddResources(XmlTypes xmlTypes = XmlTypes.All)
        {
            var assembly = AssemblyDefinition.ReadAssembly(System.Reflection.Assembly.GetCallingAssembly().GetManifestResourceStream("Destrospean.CCLoaderGeneratorLibrary.base.CCLoaderData.dll"));
            assembly.Name.Name = AssemblyName;
            assembly.MainModule.Name = AssemblyName + ".dll";
            var assemblyStream = new MemoryStream();
            // Save the assembly with the new name
            assembly.Write(assemblyStream);
            // Add the resources
            var scriptResourceKeyInstance = FNV64.GetHash(AssemblyName + ".dll");
            var nameMapResource = new NameMapResource.NameMapResource(0, null);
            nameMapResource.Add(scriptResourceKeyInstance, AssemblyName + ".dll");
            foreach (var xmlDocumentKvp in mXmlDocuments)
            {
                var xmlStream = new MemoryStream();
                xmlDocumentKvp.Value.Save(xmlStream);
                var xmlResourceKey = scriptResourceKeyInstance;
                if (((xmlTypes | XmlTypes.Data) & (XmlTypes)Enum.Parse(typeof(XmlTypes), xmlDocumentKvp.Key.Substring(xmlDocumentKvp.Key.IndexOf(".base.") + 7).Replace("._xml", ""), true)) == 0)
                {
                    continue;
                }
                if (xmlDocumentKvp.Key != kResourcePathPrefix + "data._xml")
                {
                    var xmlResourceName = AssemblyName + xmlDocumentKvp.Key.Substring(xmlDocumentKvp.Key.IndexOf(".base.") + 6).Replace("_xml", "xml");
                    xmlResourceKey = FNV64.GetHash(xmlResourceName);
                    nameMapResource.Add(xmlResourceKey, xmlResourceName);
                }
                Package.AddResource(new ResourceKey(0x333406C, 0, xmlResourceKey), xmlStream, true);
            }
            Package.AddResource(new ResourceKey(0x166038C, 0, 0), nameMapResource.Stream, true);
            Package.AddResource(new ResourceKey(0x73FAA07, 0, scriptResourceKeyInstance), new ScriptResource.ScriptResource(0, null)
                {
                    Assembly = new BinaryReader(assemblyStream)
                }.Stream, true);
        }

        public XmlDocument GetResourceAsXmlDocument(XmlTypes xmlType)
        {
            var xmlDocument = new XmlDocument();
            var xmlStream = ((APackage)Package).GetResource(GetResourceIndexEntry(xmlType));
            xmlStream.Position = 0;
            xmlDocument.Load(xmlStream);
            return xmlDocument;
        }

        public IResourceIndexEntry GetResourceIndexEntry(XmlTypes xmlType)
        {
            return Package.Find(x => x.ResourceType == 0x333406C && x.Instance == FNV64.GetHash(xmlType == XmlTypes.Data ? (AssemblyName + ".dll") : (AssemblyName + "_" + xmlType + ".xml")));
        }

        public void ReplaceXmlResource(XmlTypes xmlType, XmlDocument xmlDocument)
        {
            var resourceIndexEntry = GetResourceIndexEntry(xmlType);
            Package.DeleteResource(resourceIndexEntry);
            var stream = new MemoryStream();
            xmlDocument.Save(stream);
            stream.Position = 0;
            Package.AddResource(resourceIndexEntry, stream, true);
        }

        public void ReplaceXmlResource(XmlTypes xmlType, string xmlString)
        {
            var xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(xmlString);
            ReplaceXmlResource(xmlType, xmlDocument);
        }
    }
}
