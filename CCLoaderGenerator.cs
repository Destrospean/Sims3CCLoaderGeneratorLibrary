using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;
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

        readonly Dictionary<string, XDocument> mDocuments = new Dictionary<string, XDocument>();

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
            PopulateDocuments(typeof(CCLoaderGenerator).Assembly);
        }

        void PopulateDocuments(System.Reflection.Assembly assembly) 
        {
            foreach (var resourceName in Array.FindAll(assembly.GetManifestResourceNames(), x => x.StartsWith(kResourcePathPrefix) && x.EndsWith("._xml")))
            {
                mDocuments.Add(resourceName, XDocument.Load(assembly.GetManifestResourceStream(resourceName)));
            }
        }

        public void AddDataEntry(string name = "", string creator = "", XmlTypes xmlTypes = XmlTypes.All)
        {
            var document = GetResourceAsXmlDocument(XmlTypes.Data);
            var documentEnumerator = document.Descendants("CCLoader").GetEnumerator();
            documentEnumerator.MoveNext();
            var rootElement = documentEnumerator.Current;
            var rootEnumerator = rootElement.Descendants("Data").GetEnumerator();
            rootEnumerator.MoveNext();
            var clonedElement = new XElement(rootEnumerator.Current);
            foreach (var childElement in clonedElement.Descendants())
            {
                switch (childElement.Name.LocalName)
                {
                    case "Name":
                        childElement.Value = name;
                        break;
                    case "Creator":
                        childElement.Value = creator;
                        break;
                    case "Books_XML":
                        childElement.Value = (xmlTypes & XmlTypes.Books) == 0 ? "" : AssemblyName + "_Books.xml";
                        break;
                    case "Buffs_XML":
                        childElement.Value = (xmlTypes & XmlTypes.Buffs) == 0 ? "" : AssemblyName + "_Buffs.xml";
                        break;
                    case "EventHandlers_XML":
                        childElement.Value = (xmlTypes & XmlTypes.EventHandlers) == 0 ? "" : AssemblyName + "_EventHandlers.xml";
                        break;
                    case "Ingredients_XML":
                        childElement.Value = (xmlTypes & XmlTypes.Ingredients) == 0 ? "" : AssemblyName + "_Ingredients.xml";
                        break;
                    case "Plants_XML":
                        childElement.Value = (xmlTypes & XmlTypes.Plants) == 0 ? "" : AssemblyName + "_Plants.xml";
                        break;
                    case "Recipes_XML":
                        childElement.Value = (xmlTypes & XmlTypes.Recipes) == 0 ? "" : AssemblyName + "_Recipes.xml";
                        break;
                }
            }
            rootElement.Add(clonedElement);
            var resourceIndexEntry = GetResourceIndexEntry(XmlTypes.Data);
            Package.DeleteResource(resourceIndexEntry);
            var xmlStream = new MemoryStream();
            document.Save(xmlStream);
            Package.AddResource(resourceIndexEntry, xmlStream, true);
        }

        public void AddDataEntry(XmlTypes xmlTypes)
        {
            AddDataEntry("", "", xmlTypes);
        }

        public void AddResources(XmlTypes xmlTypes = XmlTypes.All)
        {
            var assembly = AssemblyDefinition.ReadAssembly(typeof(CCLoaderGenerator).Assembly.GetManifestResourceStream("Destrospean.CCLoaderGeneratorLibrary.base.CCLoaderData.dll"));
            assembly.Name.Name = AssemblyName;
            assembly.MainModule.Name = AssemblyName + ".dll";
            var assemblyStream = new MemoryStream();
            // Save the assembly with the new name
            assembly.Write(assemblyStream);
            // Add the resources
            var scriptResourceKeyInstance = FNV64.GetHash(AssemblyName + ".dll");
            var nameMapResource = new NameMapResource.NameMapResource(0, null);
            nameMapResource.Add(scriptResourceKeyInstance, AssemblyName + ".dll");
            foreach (var documentKvp in mDocuments)
            {
                var xmlStream = new MemoryStream();
                documentKvp.Value.Save(xmlStream);
                var xmlResourceKey = scriptResourceKeyInstance;
                if (((xmlTypes | XmlTypes.Data) & (XmlTypes)Enum.Parse(typeof(XmlTypes), documentKvp.Key.Substring(documentKvp.Key.IndexOf(".base.") + 7).Replace("._xml", ""), true)) == 0)
                {
                    continue;
                }
                if (documentKvp.Key != kResourcePathPrefix + "data._xml")
                {
                    var xmlResourceName = AssemblyName + documentKvp.Key.Substring(documentKvp.Key.IndexOf(".base.") + 6).Replace("_xml", "xml");
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

        public XDocument GetResourceAsXmlDocument(XmlTypes xmlType)
        {
            var xmlStream = ((APackage)Package).GetResource(GetResourceIndexEntry(xmlType));
            xmlStream.Position = 0;
            return XDocument.Load(xmlStream);
        }

        public IResourceIndexEntry GetResourceIndexEntry(XmlTypes xmlType)
        {
            return Package.Find(x => x.ResourceType == 0x333406C && x.Instance == FNV64.GetHash(xmlType == XmlTypes.Data ? (AssemblyName + ".dll") : (AssemblyName + "_" + xmlType + ".xml")));
        }

        public void ReplaceXmlResource(XmlTypes xmlType, XDocument document)
        {
            var resourceIndexEntry = GetResourceIndexEntry(xmlType);
            Package.DeleteResource(resourceIndexEntry);
            var xmlStream = new MemoryStream();
            document.Save(xmlStream);
            Package.AddResource(resourceIndexEntry, xmlStream, true);
        }

        public void ReplaceXmlResource(XmlTypes xmlType, string xmlString)
        {
            ReplaceXmlResource(xmlType, XDocument.Parse(xmlString));
        }
    }
}
