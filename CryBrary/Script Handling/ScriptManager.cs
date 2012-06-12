﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using CryEngine.Extensions;
using CryEngine.Sandbox;
using CryEngine.Testing;
using CryEngine.Testing.Internals;

namespace CryEngine.Initialization
{
	class ScriptManager
	{
		public ScriptManager()
		{
			if(CompiledScripts == null)
			{
				CompiledScripts = new Dictionary<ScriptType, List<CryScript>>();

				foreach(ScriptType scriptType in Enum.GetValues(typeof(ScriptType)))
					CompiledScripts.Add(scriptType, new List<CryScript>());

				FlowNodes = new List<string>();
			}

			if(!Directory.Exists(PathUtils.TempFolder))
				Directory.CreateDirectory(PathUtils.TempFolder);
			else
			{
				try
				{
					foreach(var file in Directory.GetFiles(PathUtils.TempFolder))
						File.Delete(file);
				}
				catch(UnauthorizedAccessException) { }
			}

#if !RELEASE
			TestManager.Init();
#endif
		}

		void PopulateAssemblyLookup()
		{
#if !RELEASE
			var monoDir = Path.Combine(PathUtils.EngineFolder, "Mono");
			// Doesn't exist when unit testing
			if(Directory.Exists(monoDir))
			{
				using(XmlWriter writer = XmlWriter.Create(Path.Combine(monoDir, "assemblylookup.xml")))
				{
					writer.WriteStartDocument();
					writer.WriteStartElement("AssemblyLookupTable");

					var gacFolder = Path.Combine(monoDir, "lib", "mono", "gac");
					foreach(var assemblyLocation in Directory.GetFiles(gacFolder, "*.dll", SearchOption.AllDirectories))
					{
						var separator = new string[] { "__" };
						var splitParentDir = Directory.GetParent(assemblyLocation).Name.Split(separator, StringSplitOptions.RemoveEmptyEntries);

						var assembly = Assembly.Load(Path.GetFileName(assemblyLocation) + string.Format(", Version={0}, Culture=neutral, PublicKeyToken={1}", splitParentDir.ElementAt(0), splitParentDir.ElementAt(1)));

						writer.WriteStartElement("Assembly");
						writer.WriteAttributeString("name", assembly.FullName);

						foreach(var nameSpace in assembly.GetTypes().Select(t => t.Namespace).Distinct())
						{
							writer.WriteStartElement("Namespace");
							writer.WriteAttributeString("name", nameSpace);
							writer.WriteEndElement();
						}

						writer.WriteEndElement();
					}

					writer.WriteEndElement();
					writer.WriteEndDocument();
				}
			}
#endif
		}

		bool LoadPlugins()
		{
			LoadLibrariesInFolder(Path.Combine(PathUtils.ScriptsFolder, "Plugins"));

			return true;
		}

		public void PostInit()
		{
			// These have to be registered later on due to the flow system being initialized late.
			foreach(var node in FlowNodes)
				FlowNode.Register(node);
		}

		public void OnPostScriptReload()
		{
			ForEach(ScriptType.Unknown, x => x.OnScriptReload());
		}

		/// <summary>
		/// This function will automatically scan for C# dll (*.dll) files and load the types contained within them.
		/// </summary>
		public void LoadLibrariesInFolder(string directory)
		{
			if(Directory.Exists(directory))
			{
				var plugins = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);

				if(plugins != null && plugins.Length != 0)
				{
					var typeCollection = new List<Type>();

					foreach(var plugin in plugins)
					{
						try
						{
							LoadAssembly(plugin);
						}
						//This exception tells us that the assembly isn't a valid .NET assembly for whatever reason
						catch(BadImageFormatException)
						{
							Debug.LogAlways("Plugin loading failed for {0}; dll is not valid.", plugin);
						}
					}
				}
			}
			else
				Debug.LogAlways("Skipping load of plugins in {0}, directory does not exist.", directory);
		}

		/// <summary>
		/// Processes a C# assembly and adds all found types to ScriptCompiler.CompiledScripts
		/// </summary>
		public static void ProcessAssembly(Assembly assembly)
		{
			foreach(var type in assembly.GetTypes())
				ProcessType(type);
		}

		/// <summary>
		/// Loads a C# assembly by location, creates a shadow-copy and generates debug database (mdb).
		/// </summary>
		/// <param name="assemblyPath"></param>
		public void LoadAssembly(string assemblyPath)
		{
			var newPath = Path.Combine(PathUtils.TempFolder, Path.GetFileName(assemblyPath));

			TryCopyFile(assemblyPath, ref newPath, true);

#if !RELEASE
			GenerateDebugDatabaseForAssembly(assemblyPath);

			var mdbFile = assemblyPath + ".mdb";
			if(File.Exists(mdbFile)) // success
			{
				var newMdbPath = Path.Combine(PathUtils.TempFolder, Path.GetFileName(mdbFile));
				TryCopyFile(mdbFile, ref newMdbPath);
			}
#endif

			ProcessAssembly(Assembly.LoadFrom(newPath));
		}

		void TryCopyFile(string currentPath, ref string newPath, bool overwrite = true)
		{
			if(!File.Exists(newPath))
				File.Copy(currentPath, newPath, overwrite);
			else
			{
				try
				{
					File.Copy(currentPath, newPath, overwrite);
				}
				catch(Exception ex)
				{
					if(ex is UnauthorizedAccessException || ex is IOException)
					{
						newPath = Path.ChangeExtension(newPath, "_" + Path.GetExtension(newPath));
						TryCopyFile(currentPath, ref newPath);
					}
					else
						throw;
				}
			}
		}

		/// <summary>
		/// Adds the type to CompiledScripts
		/// </summary>
		/// <param name="type"></param>
		public static CryScript ProcessType(Type type)
		{
			var script = new CryScript(type);
			if(CompiledScripts[script.ScriptType].Any(x => x.Type == type))
				return default(CryScript);

			if(!type.IsAbstract && !type.ContainsAttribute<ExcludeFromCompilationAttribute>())
			{
				switch(script.ScriptType)
				{
					case ScriptType.Actor:
						Actor.Load(ref script);
						break;
					case ScriptType.Entity:
						Entity.Load(ref script);
						break;
					case ScriptType.FlowNode:
						FlowNode.Load(ref script);
						break;
					case ScriptType.GameRules:
						GameRules.Load(ref script);
						break;
					case ScriptType.UIEventSystem:
						UIEventSystem.Load(ref script);
						break;
					case ScriptType.ScriptCompiler:
						{
							Debug.LogAlways("		Compiling scripts using {0}...", type.Name);
							var compiler = Activator.CreateInstance(type) as ScriptCompiler;
							ProcessAssembly(compiler.Compile());
						}
						break;
				}

				// When each ScriptType has been changed to a flag, only call this for CryScriptInstance types.
				ProcessMembers(type);
			}

			CompiledScripts[script.ScriptType].Add(script);

			return script;
		}

		/// <summary>
		/// Processes all members of a type for CryMono features such as CCommands.
		/// </summary>
		/// <param name="type"></param>
		public static void ProcessMembers(Type type)
		{
#if !RELEASE
			if(type.ContainsAttribute<TestCollectionAttribute>())
			{
				var ctor = type.GetConstructor(Type.EmptyTypes);
				if(ctor != null)
				{
					var collection = new TestCollection
					{
						Instance = ctor.Invoke(Type.EmptyTypes),
						Tests = from method in type.GetMethods()
								where method.ContainsAttribute<TestAttribute>()
									&& method.GetParameters().Length == 0
								select method
					};

					TestManager.TestCollections.Add(collection);
				}
			}
#endif

			SandboxExtensionAttribute attr;
			if(type.TryGetAttribute(out attr) && type.Implements<Form>())
			{
				Debug.LogAlways("Registering Sandbox extension: {0}", attr.Name);
				FormHelper.AvailableForms.Add(new FormInfo { Type = type, Data = attr });
			}

			foreach(var member in type.GetMembers(BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.Public))
			{
				switch(member.MemberType)
				{
					case MemberTypes.Method:
						{
							CCommandAttribute attribute;
							if(member.TryGetAttribute<CCommandAttribute>(out attribute))
								CCommand.Register(attribute.Name ?? member.Name, Delegate.CreateDelegate(typeof(CCommandDelegate), member as MethodInfo) as CCommandDelegate, attribute.Comment, attribute.Flags);
						}
						break;
					case MemberTypes.Field:
					case MemberTypes.Property:
						{
							CVarAttribute attribute;
							if(member.TryGetAttribute<CVarAttribute>(out attribute))
							{
								// There's no way to pass the variable itself by reference to properly register the CVar.
								//CVar.Register(attribute, member, 
							}
						}
						break;
				}
			}
		}

		public void GenerateDebugDatabaseForAssembly(string assemblyPath)
		{
			if(File.Exists(Path.ChangeExtension(assemblyPath, "pdb")))
			{
				var assembly = Assembly.LoadFrom(Path.Combine(PathUtils.EngineFolder, "Mono", "bin", "pdb2mdb.dll"));
				var driver = assembly.GetType("Driver");
				var convertMethod = driver.GetMethod("Convert", BindingFlags.Static | BindingFlags.Public);

				object[] args = { assemblyPath };
				convertMethod.Invoke(null, args);
			}
		}

		internal static List<string> FlowNodes { get; set; }

		#region Statics
		/// <summary>
		/// Called once per frame.
		/// </summary>
		public static void OnUpdate(float frameTime)
		{
			Time.DeltaTime = frameTime;

			foreach(var scriptList in CompiledScripts)
			{
				if(scriptList.Key == ScriptType.Unknown)
					continue;

				scriptList.Value.ForEach(script =>
					{
						if(script.ScriptInstances != null)
						{
							script.ScriptInstances.ForEach(instance =>
								{
									if(instance.ReceiveUpdates)
										instance.OnUpdate();
								});
						}
					});
			}
		}

		/*
		public static IEnumerable<CryScript> GetScriptList(ScriptType scriptType)
		{
			if(!Enum.IsDefined(typeof(ScriptType), scriptType))
				throw new ArgumentException(string.Format("scriptType: value {0} was not defined in the enum", scriptType));

			if(scriptType == ScriptType.Unknown)
			{
				var newList = new List<CryScript>();

				foreach(var scriptList in CompiledScripts)
				{
					if(scriptList.Value != null)
						newList.AddRange(scriptList.Value);
				}

				return newList;
			}

			return CompiledScripts[scriptType];
		}*/

		/// <summary>
		/// Instantiates a script using its name and interface.
		/// </summary>
		/// <param name="scriptName"></param>
		/// <param name="constructorParams"></param>
		/// <returns>New instance scriptId or -1 if instantiation failed.</returns>
		public static object InstantiateScript(string scriptName, ScriptType scriptType, object[] constructorParams = null)
		{
			if(scriptName == null)
				throw new ArgumentNullException("scriptName");
			else if(scriptName.Length < 1)
				throw new ArgumentException("Empty script name passed to InstantiateClass");
			else if(!Enum.IsDefined(typeof(ScriptType), scriptType))
				throw new ArgumentException(string.Format("scriptType: value {0} was not defined in the enum", scriptType));

			var script = FirstOrDefaultScript(scriptType, x => x.ScriptName.Equals(scriptName) || (x.ScriptName.Contains(scriptName) && x.Type.Name.Equals(scriptName)));
			if(script == default(CryScript))
				throw new ScriptNotFoundException(string.Format("Script {0} of ScriptType {1} could not be found.", scriptName, scriptType));

			var scriptInstance = Activator.CreateInstance(script.Type, constructorParams);
			AddScriptInstance(scriptInstance as CryScriptInstance, script);

			if(scriptType == ScriptType.GameRules)
				GameRules.Current = (GameRules)scriptInstance;

			return scriptInstance;
		}

		public static void RemoveInstance(int scriptId, ScriptType scriptType)
		{
			RemoveInstance(scriptId, scriptType, null);
		}

		/// <summary>
		/// Locates and destructs the script with the assigned scriptId.
		/// </summary>
		/// <param name="scriptId"></param>
		public static void RemoveInstance(int scriptId, ScriptType scriptType, Type type = null)
		{
			if(!Enum.IsDefined(typeof(ScriptType), scriptType))
				throw new ArgumentException(string.Format("scriptType: value {0} was not defined in the enum", scriptType));

			if(type != null)
			{
				var script = FirstOrDefaultScript(scriptType, x => x.Type == type);
				if(script == default(CryScript))
					throw new TypeLoadException(string.Format("Failed to locate type {0}", type.Name));

				if(script.ScriptInstances != null && script.ScriptInstances.RemoveAll(x => x.ScriptId == scriptId) > 0)
				{
					int index = CompiledScripts[script.ScriptType].FindIndex(x => x.Type == script.Type);
					CompiledScripts[script.ScriptType].Insert(index, script);
				}
			}
			else
			{
				ForEachScript(scriptType, script =>
					{
						if(script.ScriptInstances != null && script.ScriptInstances.RemoveAll(x => x.ScriptId == scriptId) > 0)
						{
							int index = CompiledScripts[script.ScriptType].FindIndex(x => x.Type == script.Type);
							CompiledScripts[script.ScriptType].Insert(index, script);
						}
					});
			}


		}

		public static int GetEntityScriptId(EntityId entityId, ScriptType scriptType)
		{
			if(!Enum.IsDefined(typeof(ScriptType), scriptType))
				throw new ArgumentException(string.Format("scriptType: value {0} was not defined in the enum", scriptType));

			var scriptId = -1;
			ForEachScript(scriptType, script =>
				{
					if(script.ScriptInstances != null)
					{
						script.ScriptInstances.ForEach(scriptInstance =>
							{
								var scriptEntity = scriptInstance as Entity;
								if(scriptEntity != null && scriptEntity.Id == entityId)
								{
									scriptId = scriptEntity.Id;
									return;
								}
							});

						if(scriptId != -1)
							return;
					}
				});

			return scriptId;
		}

		public static CryScriptInstance GetScriptInstanceById(int id, ScriptType scriptType)
		{
			if(!Enum.IsDefined(typeof(ScriptType), scriptType))
				throw new ArgumentException(string.Format("scriptType: value {0} was not defined in the enum", scriptType));

			CryScriptInstance scriptInstance = null;
			ForEach(scriptType, instance =>
			{
				if(instance.ScriptId == id)
				{
					scriptInstance = instance;
					return;
				}
			});

			return scriptInstance;
		}

		#region Linq statements
		public static CryScript FirstOrDefaultScript(ScriptType scriptType, Func<CryScript, bool> predicate)
		{
			var defaultScript = default(CryScript);

			if(scriptType == ScriptType.Unknown)
			{
				foreach(var scriptPair in CompiledScripts)
				{
					var script = scriptPair.Value.FirstOrDefault(predicate);
					if(script != defaultScript)
						return script;
				}
			}
			else
				return CompiledScripts[scriptType].FirstOrDefault(predicate);

			return defaultScript;
		}

		public static void ForEachScript(ScriptType scriptType, Action<CryScript> action)
		{
			if(scriptType == ScriptType.Unknown)
			{
				foreach(var scriptPair in CompiledScripts)
					scriptPair.Value.ForEach(action);
			}
			else
				CompiledScripts[scriptType].ForEach(action);
		}

		public static void ForEach(ScriptType scriptType, Action<CryScriptInstance> action)
		{
			ForEachScript(scriptType, script =>
			{
				if(script.ScriptInstances != null)
					script.ScriptInstances.ForEach(action);
			});
		}

		public static CryScriptInstance First(ScriptType scriptType, Func<CryScriptInstance, bool> predicate)
		{
			CryScriptInstance scriptInstance = null;

			ForEachScript(scriptType, script =>
				{
					var instance = script.ScriptInstances.First(predicate);
					if(instance != null)
					{
						scriptInstance = instance;
						return;
					}
				});

			return scriptInstance;
		}

		public static T Find<T>(ScriptType scriptType, Predicate<T> match) where T : CryScriptInstance
		{
			T result = null;
			ForEachScript(scriptType, script =>
				{
					if((script.Type.Equals(typeof(T)) || script.Type.Implements(typeof(T))) && script.ScriptInstances != null)
					{
						var foundInstance = script.ScriptInstances.Find(x => match(x as T)) as T;
						if(foundInstance != null)
						{
							result = foundInstance;
							return;
						}
					}
				});

			return result;
		}
		#endregion

		public static void AddScriptInstance(CryScriptInstance instance)
		{
			if(instance == null)
				throw new ArgumentNullException("instance");

			var script = FirstOrDefaultScript(ScriptType.Unknown, x => x.Type == instance.GetType());
			if(script == default(CryScript))
				script = ProcessType(instance.GetType());

			AddScriptInstance(instance, script);
		}

		public static void AddScriptInstance(CryScriptInstance instance, ScriptType scriptType)
		{
			if(instance == null)
				throw new ArgumentNullException("instance");
			else if(!Enum.IsDefined(typeof(ScriptType), scriptType))
				throw new ArgumentException(string.Format("scriptType: value {0} was not defined in the enum", scriptType));

			var script = FirstOrDefaultScript(scriptType, x => x.Type == instance.GetType());
			if(script == default(CryScript))
				script = ProcessType(instance.GetType());

			AddScriptInstance(instance, script);
		}

		/// <summary>
		/// Adds an script instance to the script collection and returns its new id.
		/// </summary>
		/// <param name="instance"></param>
		public static void AddScriptInstance(CryScriptInstance instance, CryScript script)
		{
			if(instance == null)
				throw new ArgumentNullException("instance");

			instance.ScriptId = LastScriptId++;

			if(script.ScriptInstances == null)
				script.ScriptInstances = new List<CryScriptInstance>();

			script.ScriptInstances.Add(instance);

			int index = CompiledScripts[script.ScriptType].FindIndex(x => x.Type == script.Type);
			CompiledScripts[script.ScriptType].Insert(index, script);
		}

		/// <summary>
		/// Last assigned ScriptId, next = + 1
		/// </summary>
		public static int LastScriptId = 1;

		// TODO: ScriptType -> flags, better way of storing to allow fast searching of multiple ScriptTypes simultaneously. (I.e. when using Entity.Get with an actors entity id.
		internal static Dictionary<ScriptType, List<CryScript>> CompiledScripts { get; set; }
		#endregion
	}

	[Serializable]
	public class ScriptNotFoundException : Exception
	{
		public ScriptNotFoundException(string error)
		{
			message = error;
		}

		private string message;
		public override string Message
		{
			get { return message; }
		}
	}
}