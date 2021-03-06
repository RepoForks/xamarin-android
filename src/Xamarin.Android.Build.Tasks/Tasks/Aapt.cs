﻿// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Xamarin.Android.Build.Utilities;

namespace Xamarin.Android.Tasks
{
	public class Aapt : ToolTask
	{
		private const string error_regex_string = @"(?<file>.*)\s*:\s*(?<line>\d*)\s*:\s*error:\s*(?<error>.+)";
		private Regex error_regex = new Regex (error_regex_string, RegexOptions.Compiled);

		public ITaskItem[] AdditionalAndroidResourcePaths { get; set; }

		public string AndroidComponentResgenFlagFile { get; set; }

		public bool NonConstantId { get; set; }

		public string AssetDirectory { get; set; }

		[Required]
		public string ManifestFile { get; set; }

		[Required]
		public string ResourceDirectory { get; set; }

		public string ResourceOutputFile { get; set; }

		[Required]
		public string JavaDesignerOutputDirectory { get; set; }

		[Required]
		public string JavaPlatformJarPath { get; set; }

		public string UncompressedFileExtensions { get; set; }
		public string PackageName { get; set; }

		[Required]
		public string ApplicationName { get; set; }

		public string ExtraPackages { get; set; }

		public ITaskItem [] AdditionalResourceDirectories { get; set; }

		public ITaskItem [] LibraryProjectJars { get; set; }

		public string ExtraArgs { get; set; }

		protected override string ToolName { get { return OS.IsWindows ? "aapt.exe" : "aapt"; } }

		public string ApiLevel { get; set; }

		public bool AndroidUseLatestPlatformSdk { get; set; }

		public string SupportedAbis { get; set; }

		public bool CreatePackagePerAbi { get; set; }

		public string ImportsDirectory { get; set; }
		public string OutputImportDirectory { get; set; }
		public bool UseShortFileNames { get; set; }

		public string ResourceNameCaseMap { get; set; }

		public bool ExplicitCrunch { get; set; }

		string currentAbi;
		string currentResourceOutputFile;
		Dictionary<string,string> resource_name_case_map = new Dictionary<string,string> ();

		bool ManifestIsUpToDate (string manifestFile)
		{
			return !String.IsNullOrEmpty (AndroidComponentResgenFlagFile) &&
				File.Exists (AndroidComponentResgenFlagFile) && File.Exists (manifestFile) &&
				File.GetLastWriteTime (AndroidComponentResgenFlagFile) > File.GetLastWriteTime (manifestFile);
		}

		bool ExecuteForAbi (string abi)
		{
			currentAbi = abi;
			var ret = base.Execute ();
			if (ret && !string.IsNullOrEmpty (currentResourceOutputFile)) {
				var tmpfile = currentResourceOutputFile + ".bk";
				MonoAndroidHelper.CopyIfZipChanged (tmpfile, currentResourceOutputFile);
				File.Delete (tmpfile);
			}
			return ret;
		}

		public override bool Execute ()
		{
			Log.LogDebugMessage ("Aapt Task");
			Log.LogDebugMessage ("  AssetDirectory: {0}", AssetDirectory);
			Log.LogDebugMessage ("  ManifestFile: {0}", ManifestFile);
			Log.LogDebugMessage ("  ResourceDirectory: {0}", ResourceDirectory);
			Log.LogDebugMessage ("  JavaDesignerOutputDirectory: {0}", JavaDesignerOutputDirectory);
			Log.LogDebugMessage ("  PackageName: {0}", PackageName);
			Log.LogDebugMessage ("  UncompressedFileExtensions: {0}", UncompressedFileExtensions);
			Log.LogDebugMessage ("  ExtraPackages: {0}", ExtraPackages);
			Log.LogDebugTaskItems ("  AdditionalResourceDirectories: ", AdditionalResourceDirectories);
			Log.LogDebugTaskItems ("  AdditionalAndroidResourcePaths: ", AdditionalAndroidResourcePaths);
			Log.LogDebugTaskItems ("  LibraryProjectJars: ", LibraryProjectJars);
			Log.LogDebugMessage ("  ExtraArgs: {0}", ExtraArgs);
			Log.LogDebugMessage ("  CreatePackagePerAbi: {0}", CreatePackagePerAbi);
			Log.LogDebugMessage ("  ResourceNameCaseMap: {0}", ResourceNameCaseMap);
			if (CreatePackagePerAbi)
				Log.LogDebugMessage ("  SupportedAbis: {0}", SupportedAbis);

			bool upToDate = ManifestIsUpToDate (ManifestFile);

			if (ResourceNameCaseMap != null)
				foreach (var arr in ResourceNameCaseMap.Split (';').Select (l => l.Split ('|')).Where (a => a.Length == 2))
					resource_name_case_map [arr [1]] = arr [0]; // lowercase -> original

			if (AdditionalAndroidResourcePaths != null)
				foreach (var dir in AdditionalAndroidResourcePaths)
					if (!string.IsNullOrEmpty (dir.ItemSpec))
						upToDate = upToDate && ManifestIsUpToDate (string.Format ("{0}{1}{2}", dir, Path.DirectorySeparatorChar, "manifest", Path.DirectorySeparatorChar, "AndroidManifest.xml"));

			if (upToDate) {
				Log.LogMessage (MessageImportance.Normal, "  Additional Android Resources manifsets files are unchanged. Skipping.");
				return true;
			}

			ExecuteForAbi (null);

			if (CreatePackagePerAbi) {
				var abis = SupportedAbis.Split (new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
				if (abis.Length > 1)
					foreach (var abi in abis)
						ExecuteForAbi (abi);
			}
			return !Log.HasLoggedErrors;
		}

		protected override string GenerateCommandLineCommands ()
		{
			// For creating Resource.Designer.cs:
			//   Running command: C:\Program Files (x86)\Android\android-sdk-windows\platform-tools\aapt
			//     "package"
			//     "-M" "C:\Users\Jonathan\AppData\Local\Temp\ryob4gaw.way\AndroidManifest.xml"
			//     "-J" "C:\Users\Jonathan\AppData\Local\Temp\ryob4gaw.way"
			//     "-F" "C:\Users\Jonathan\AppData\Local\Temp\ryob4gaw.way\resources.apk"
			//     "-S" "c:\users\jonathan\documents\visual studio 2010\Projects\MonoAndroidApplication4\MonoAndroidApplication4\obj\Debug\res"
			//     "-I" "C:\Program Files (x86)\Android\android-sdk-windows\platforms\android-8\android.jar"
			//     "--max-res-version" "10"

			// For packaging:
			//   Running command: C:\Program Files (x86)\Android\android-sdk-windows\platform-tools\aapt
			//     "package"
			//     "-f"
			//     "-m"
			//     "-M" "AndroidManifest.xml"
			//     "-J" "src"
			//     "--custom-package" "androidmsbuildtest.androidmsbuildtest"
			//     "-F" "bin\packaged_resources"
			//     "-S" "C:\Users\Jonathan\Documents\Visual Studio 2010\Projects\AndroidMSBuildTest\AndroidMSBuildTest\obj\Debug\res"
			//     "-I" "C:\Program Files (x86)\Android\android-sdk-windows\platforms\android-8\android.jar"
			//     "--extra-packages" "com.facebook.android:my.another.library"

			var cmd = new CommandLineBuilder ();

			cmd.AppendSwitch ("package");

			if (MonoAndroidHelper.LogInternalExceptions)
				cmd.AppendSwitch ("-v");
			if (NonConstantId)
				cmd.AppendSwitch ("--non-constant-id");
			cmd.AppendSwitch ("-f");
			cmd.AppendSwitch ("-m");
			string manifestFile;
			string manifestDir = Path.Combine (Path.GetDirectoryName (ManifestFile), currentAbi != null ? currentAbi : "manifest");
			Directory.CreateDirectory (manifestDir);
			manifestFile = Path.Combine (manifestDir, Path.GetFileName (ManifestFile));
			ManifestDocument manifest = new ManifestDocument (ManifestFile, this.Log);
			if (currentAbi != null)	
				manifest.SetAbi (currentAbi);
			manifest.ApplicationName = ApplicationName;
			manifest.Save (manifestFile);
			currentResourceOutputFile = currentAbi != null ? string.Format ("{0}-{1}", ResourceOutputFile, currentAbi) : ResourceOutputFile;

			cmd.AppendSwitchIfNotNull ("-M ", manifestFile);
			Directory.CreateDirectory (JavaDesignerOutputDirectory);
			cmd.AppendSwitchIfNotNull ("-J ", JavaDesignerOutputDirectory);

			if (PackageName != null)
				cmd.AppendSwitchIfNotNull ("--custom-package ", PackageName.ToLowerInvariant ());

			if (!string.IsNullOrEmpty (currentResourceOutputFile))
				cmd.AppendSwitchIfNotNull ("-F ", currentResourceOutputFile + ".bk");
			// The order of -S arguments is *important*, always make sure this one comes FIRST
			cmd.AppendSwitchIfNotNull ("-S ", ResourceDirectory.TrimEnd ('\\'));
			if (AdditionalResourceDirectories != null)
				foreach (var resdir in AdditionalResourceDirectories)
					cmd.AppendSwitchIfNotNull ("-S ", resdir.ItemSpec.TrimEnd ('\\'));
			if (AdditionalAndroidResourcePaths != null)
				foreach (var dir in AdditionalAndroidResourcePaths)
					cmd.AppendSwitchIfNotNull ("-S ", Path.Combine (dir.ItemSpec.TrimEnd (System.IO.Path.DirectorySeparatorChar), "res"));

			if (LibraryProjectJars != null)
				foreach (var jar in LibraryProjectJars)
					cmd.AppendSwitchIfNotNull ("-j ", jar);
			
			cmd.AppendSwitchIfNotNull ("-I ", JavaPlatformJarPath);

			// Add asset directory if it exists
			if (!string.IsNullOrWhiteSpace (AssetDirectory) && Directory.Exists (AssetDirectory))
				cmd.AppendSwitchIfNotNull ("-A ", AssetDirectory.TrimEnd ('\\'));

			if (!string.IsNullOrWhiteSpace (UncompressedFileExtensions))
				foreach (var ext in UncompressedFileExtensions.Split (new char[] { ';', ','}, StringSplitOptions.RemoveEmptyEntries))
					cmd.AppendSwitchIfNotNull ("-0 ", ext);

			if (!string.IsNullOrEmpty (ExtraPackages))
				cmd.AppendSwitchIfNotNull ("--extra-packages ", ExtraPackages);

			// TODO: handle resource names
			if (ExplicitCrunch)
				cmd.AppendSwitch ("--no-crunch");

			cmd.AppendSwitch ("--auto-add-overlay");

			var extraArgsExpanded = ExpandString (ExtraArgs);
			if (extraArgsExpanded != ExtraArgs)
				Log.LogDebugMessage ("  ExtraArgs expanded: {0}", extraArgsExpanded);

			if (!string.IsNullOrWhiteSpace (extraArgsExpanded))
				cmd.AppendSwitch (extraArgsExpanded);

			if (!AndroidUseLatestPlatformSdk)
				cmd.AppendSwitchIfNotNull ("--max-res-version ", ApiLevel);

			return cmd.ToString ();
		}

		string ExpandString (string s)
		{
			if (s == null)
				return null;
			int start = 0;
			int st = s.IndexOf ("${library.imports:", start, StringComparison.Ordinal);
			if (st >= 0) {
				int ed = s.IndexOf ('}', st);
				if (ed < 0)
					return s.Substring (0, st + 1) + ExpandString (s.Substring (st + 1));
				int ast = st + "${library.imports:".Length;
				string aname = s.Substring (ast, ed - ast);
				return s.Substring (0, st) + Path.Combine (OutputImportDirectory, UseShortFileNames ? MonoAndroidHelper.GetLibraryImportDirectoryNameForAssembly (aname) : aname, ImportsDirectory) + Path.DirectorySeparatorChar + ExpandString (s.Substring (ed + 1));
			}
			else
				return s;
		}

		protected override string GenerateFullPathToTool ()
		{
			return Path.Combine (ToolPath, ToolExe);
		}

		protected override void LogEventsFromTextOutput (string singleLine, MessageImportance messageImportance)
		{
			// Aapt errors looks like this:
			//   C:\Users\Jonathan\Documents\Visual Studio 2010\Projects\AndroidMSBuildTest\AndroidMSBuildTest\obj\Debug\res\layout\main.axml:7: error: No resource identifier found for attribute 'id2' in package 'android' (TaskId:22)
			// Look for them and convert them to MSBuild compatible errors.
			
			var match = error_regex.Match (singleLine);

			if (match.Success) {
				var file = match.Groups["file"].Value;
				var line = int.Parse (match.Groups["line"].Value) + 1;
				var error = match.Groups["error"].Value;

				// Try to map back to the original resource file, so when the user
				// double clicks the error, it won't take them to the obj/Debug copy
				if (file.StartsWith (ResourceDirectory, StringComparison.InvariantCultureIgnoreCase)) {
					file = file.Substring (ResourceDirectory.Length);
					file = resource_name_case_map.ContainsKey (file) ? resource_name_case_map [file] : file;
					file = Path.Combine ("Resources", file);
				}

				// Strip any "Error:" text from aapt's output
				if (error.StartsWith ("error: ", StringComparison.InvariantCultureIgnoreCase))
					error = error.Substring ("error: ".Length);

				singleLine = string.Format ("{0}({1}): error APT0000: {2}", file, line, error);
				messageImportance = MessageImportance.High;
			}

			// Handle additional error that doesn't match the regex
			if (singleLine.Trim ().StartsWith ("invalid resource directory name:")) {
				Log.LogError ("", "", "", ToolName, -1, -1, -1, -1, "Invalid resource directory name: \"{0}\".", singleLine.Substring (singleLine.LastIndexOfAny (new char[] { '\\', '/' }) + 1));
				messageImportance = MessageImportance.High;
			}

			base.LogEventsFromTextOutput (singleLine, messageImportance);
		}
	}
}
