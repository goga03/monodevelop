// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Mike Krüger" email="mike@icsharpcode.net"/>
//     <version value="$version"/>
// </file>

using System;
using System.Collections;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Xml;

using MonoDevelop.Core.Properties;

namespace MonoDevelop.Core.AddIns
{
	/// <summary>
	/// Default implementation for the <see cref="IAddInTree"/> interface.
	/// </summary>
	public class DefaultAddInTree : IAddInTree
	{
		AssemblyLoader loader;
		AddInCollection addIns = new AddInCollection();
		
		DefaultAddInTreeNode  root = new DefaultAddInTreeNode();
		
		ConditionFactory conditionFactory = new ConditionFactory();
		CodonFactory    codonFactory    = new CodonFactory();
				
		/// <summary>
		/// Returns the default condition factory. ICondition objects
		/// are created only with this factory during the tree 
		/// construction process.
		/// </summary>
		public ConditionFactory ConditionFactory {
			get {
				return conditionFactory;
			}
		}

		/// <summary>
		/// Returns the default codon factory. ICodon objects
		/// are created only with this factory during the tree 
		/// construction process.
		/// </summary>
		public CodonFactory CodonFactory {
			get {
				return codonFactory;
			}
		}
		
		internal AssemblyLoader Loader {
			get { return loader; }
		}

		public bool TreeNodeExists (string path)
		{
			if (path == null || path.Length == 0)
				return false;
			string[] splitPath = path.Split('/');
			IAddInTreeNode curPath = root;
			int i = 0;
			while (i < splitPath.Length) {
				if (!curPath.ChildNodes.ContainsKey(splitPath[i])) {
					return false;
				}
				curPath = curPath.ChildNodes[splitPath[i]] as IAddInTreeNode;
				i++;
			}
			return true;
		}
		
		/// <summary>
		/// Returns a collection of all loaded add ins.
		/// </summary>
		public AddInCollection AddIns {
			get {
				return addIns;
			}
		}
		
		/// <summary>
		/// Constructs a new instance of the <code>DefaultAddInTree</code> object.
		/// </summary>
		internal DefaultAddInTree (AssemblyLoader loader)
		{
			this.loader = loader;
		}
		
		void ShowCodonTree(IAddInTreeNode node, string ident)
		{
			foreach (DictionaryEntry entry in node.ChildNodes) {
				Console.WriteLine(ident + entry.Key);
				ShowCodonTree((IAddInTreeNode)entry.Value, ident + '\t');
			}
		}
		
		/// <summary>
		/// Prints the tree codons to the console. (for debug purposes)
		/// </summary>
		public void ShowCodonTree()
		{
			ShowCodonTree(root, "");
		}
		
		void AddExtensions (AddIn addIn, AddIn.Extension extension)
		{
			DefaultAddInTreeNode localRoot = CreatePath(root, extension.Path);
			if (extension.AllowedChildNodes != null && extension.AllowedChildNodes.Length > 0) {
				localRoot.AllowedChildNodes = extension.AllowedChildNodes;
				localRoot.OwnerAddIn = addIn;
			}
			if (extension.Description != null)
				localRoot.Description = extension.Description;
			
			foreach (ICodon codon in extension.CodonCollection) {
				DefaultAddInTreeNode localPath = CreatePath(localRoot, codon.ID);
				if (localPath.Codon != null) {
					throw new DuplicateCodonException(codon.GetType().Name, codon.ID);
				}
				localPath.Codon = codon;
				localPath.ConditionCollection = (ConditionCollection)extension.Conditions[codon.ID];
				localPath.AllowedChildNodes = CodonBuilder.GetAllowedChildNodes (codon.GetType ());
//				localPath.Description = CodonBuilder.GetDescription (codon.GetType ());
			}
		}
		
		/// <summary>
		/// Add a <see cref="AddIn"/> object to the tree, inserting all it's extensions.
		/// </summary>
		public void InsertAddIn(AddIn addIn)
		{
			addIns.Add(addIn);
			foreach (AddIn.Extension extension in addIn.Extensions) {
				AddExtensions (addIn, extension);
			}
		}
		
		/// <summary>
		/// Removes an AddIn from the AddInTree.
		/// </summary>
		public void RemoveAddIn(AddIn addIn)
		{ // TODO : Implement the RemoveAddInMethod
			throw new ApplicationException("Implement ME!");
		}
		
		
		DefaultAddInTreeNode CreatePath(DefaultAddInTreeNode localRoot, string path)
		{
			if (path == null || path.Length == 0) {
				return localRoot;
			}
			string[] splittedPath = path.Split(new char[] {'/'});
			DefaultAddInTreeNode curPath = localRoot;
			int      i = 0;
			
			while (i < splittedPath.Length) {
				DefaultAddInTreeNode nextPath = (DefaultAddInTreeNode)curPath.ChildNodes[splittedPath[i]];
				if (nextPath == null) {
					nextPath = new DefaultAddInTreeNode();
					curPath.AddNode (splittedPath[i], nextPath);
				}
				curPath = nextPath;
				++i;
			}
			
			return curPath;
		}
		
		/// <summary>
		/// Searches a requested path and returns the TreeNode in this path as value.
		/// If path is <code>null</code> or path.Length is zero the root node is returned.
		/// </summary>
		/// <param name="path">
		/// The path inside the tree structure.
		/// </param>
		/// <exception cref="TreePathNotFoundException">
		/// Is thrown when the path is not found in the tree.
		/// </exception>
		public IAddInTreeNode GetTreeNode(string path)
		{
			if (path == null || path.Length == 0) {
				return root;
			}
			
			string[] splittedPath = path.Split(new char[] {'/'});
			DefaultAddInTreeNode curPath = root;
			int i = 0;
			
			while (i < splittedPath.Length) {
				DefaultAddInTreeNode nextPath = (DefaultAddInTreeNode)curPath.ChildNodes[splittedPath[i]];
				if (nextPath == null) {
					throw new TreePathNotFoundException(path);
				}
				curPath = nextPath;
				++i;
			}
			
			return curPath;
		}
		
		Hashtable registeredAssemblies = new Hashtable();
			
			
		/// <summary>
		/// This method loads an assembly and gets all 
		/// it's defined codons and conditions
		/// </summary>
		public Assembly LoadAssembly (string fileName)
		{
			Assembly assembly = (Assembly)registeredAssemblies[fileName];
			
			if (assembly == null) {
				Assembly asm = loader.LoadAssembly (fileName);
				registeredAssemblies[fileName] = assembly = asm;
				LoadCodonsAndConditions(assembly);
			}
			
			return assembly;
		}

		/// <summary>
		/// This method does load all codons and conditions in the given assembly.
		/// It will create builders for them which could be used by the factories to
		/// create the codon and condition objects.
		/// </summary>
		void LoadCodonsAndConditions(Assembly assembly)
		{
			foreach(Type type in assembly.GetTypes()) {
				if (!type.IsAbstract) {
					if (type.IsSubclassOf(typeof(AbstractCodon)) && Attribute.GetCustomAttribute(type, typeof(CodonNameAttribute)) != null) {
						codonFactory.AddCodonBuilder(new CodonBuilder(type.FullName, assembly));
					} else if (type.IsSubclassOf(typeof(AbstractCondition)) && Attribute.GetCustomAttribute(type, typeof(ConditionAttribute)) != null) {
						conditionFactory.Builders.Add(new ConditionBuilder(type.FullName, assembly));
					}
				}
			}
		}
	}
}
