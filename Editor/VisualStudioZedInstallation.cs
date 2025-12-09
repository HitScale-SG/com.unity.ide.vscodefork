/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml.XPath;
using UnityEngine;
using IOPath = System.IO.Path;
using Debug = UnityEngine.Debug;

namespace Microsoft.Unity.VisualStudio.Editor
{
	internal class VisualStudioZedInstallation : VisualStudioInstallation
	{
		private const string DefaultSettingsContent = @"{
    ""file_scan_exclusions"": [
        ""**/.*"",
        ""**/*~"",
        ""*.csproj"",
        ""*.sln"",
        ""**/*.meta"",
        ""**/*.booproj"",
        ""**/*.pibd"",
        ""**/*.suo"",
        ""**/*.user"",
        ""**/*.userprefs"",
        ""**/*.unityproj"",
        ""**/*.dll"",
        ""**/*.exe"",
        ""**/*.pdf"",
        ""**/*.mid"",
        ""**/*.midi"",
        ""**/*.wav"",
        ""**/*.gif"",
        ""**/*.ico"",
        ""**/*.jpg"",
        ""**/*.jpeg"",
        ""**/*.png"",
        ""**/*.psd"",
        ""**/*.tga"",
        ""**/*.tif"",
        ""**/*.tiff"",
        ""**/*.3ds"",
        ""**/*.3DS"",
        ""**/*.fbx"",
        ""**/*.FBX"",
        ""**/*.lxo"",
        ""**/*.LXO"",
        ""**/*.ma"",
        ""**/*.MA"",
        ""**/*.obj"",
        ""**/*.OBJ"",
        ""**/*.asset"",
        ""**/*.cubemap"",
        ""**/*.flare"",
        ""**/*.mat"",
        ""**/*.prefab"",
        ""**/*.unity"",
        ""build/"",
        ""Build/"",
        ""library/"",
        ""Library/"",
        ""obj/"",
        ""Obj/"",
        ""ProjectSettings/"",
        ""UserSettings/"",
        ""temp/"",
        ""Temp/"",
        ""logs"",
        ""Logs""
    ]
}";

		private static readonly IGenerator _generator = new SdkStyleProjectGeneration();

		public override bool SupportsAnalyzers
		{
			get
			{
				return false;
			}
		}

		public override Version LatestLanguageVersionSupported
		{
			get
			{
				return new Version(11, 0);
			}
		}

		public override string[] GetAnalyzers()
		{
			return Array.Empty<string>();
		}

		public override IGenerator ProjectGenerator
		{
			get
			{
				return _generator;
			}
		}

		private static bool IsCandidateForDiscovery(string path)
		{
#if UNITY_EDITOR_OSX
			if (Directory.Exists(path) && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
				return path.IndexOf("zed", StringComparison.OrdinalIgnoreCase) >= 0;

			return File.Exists(path) && IOPath.GetFileName(path).IndexOf("zed", StringComparison.OrdinalIgnoreCase) >= 0;
#else
			return File.Exists(path) && IOPath.GetFileName(path).IndexOf("zed", StringComparison.OrdinalIgnoreCase) >= 0;
#endif
		}

		private static bool TryGetVersionFromPlist(string manifestBase, out Version version)
		{
			version = null;

			var plistPath = IOPath.Combine(manifestBase, "Info.plist");
			if (!File.Exists(plistPath))
				return false;

			try
			{
				var xPath = new XPathDocument(plistPath);
				var navigator = xPath.CreateNavigator().SelectSingleNode("/plist/dict/key[text()='CFBundleShortVersionString']/following-sibling::string[1]/text()");
				if (navigator == null)
					return false;

				return Version.TryParse(navigator.Value, out version);
			}
			catch
			{
				return false;
			}
		}

		public static bool TryDiscoverInstallation(string editorPath, out IVisualStudioInstallation installation)
		{
			installation = null;

			if (string.IsNullOrEmpty(editorPath))
				return false;

			if (!IsCandidateForDiscovery(editorPath))
				return false;

			var resolvedPath = GetRealPath(editorPath);
			var name = new StringBuilder("Zed");
			Version version = null;

#if UNITY_EDITOR_OSX
			var manifestBase = resolvedPath;

			if (Directory.Exists(manifestBase) && manifestBase.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
			{
				manifestBase = IOPath.Combine(manifestBase, "Contents");
			}
			else
			{
				var parent = Directory.GetParent(manifestBase);

				if (parent != null && string.Equals(parent.Name, "MacOS", StringComparison.OrdinalIgnoreCase))
					manifestBase = parent.Parent?.FullName;
				else
					manifestBase = parent?.FullName;

				if (!string.IsNullOrEmpty(manifestBase) && !manifestBase.EndsWith("Contents", StringComparison.OrdinalIgnoreCase))
					manifestBase = IOPath.Combine(manifestBase, "Contents");
			}

			if (!string.IsNullOrEmpty(manifestBase) && TryGetVersionFromPlist(manifestBase, out version))
				name.Append($" [{version.ToString(3)}]");
#endif

			installation = new VisualStudioZedInstallation()
			{
				IsPrerelease = false,
				Name = name.ToString(),
				Path = editorPath,
				Version = version ?? new Version()
			};

			return true;
		}

		public static IEnumerable<IVisualStudioInstallation> GetVisualStudioInstallations()
		{
			var candidates = new List<string>();

#if UNITY_EDITOR_WIN
			// Zed is not officially available on Windows yet.
#elif UNITY_EDITOR_OSX
			var appPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
			if (Directory.Exists(appPath))
				candidates.AddRange(Directory.EnumerateDirectories(appPath, "Zed*.app"));

			candidates.Add("/Applications/Zed.app");
			candidates.Add("/usr/local/bin/zed");
#elif UNITY_EDITOR_LINUX
			candidates.Add("/usr/bin/zed");
			candidates.Add("/usr/local/bin/zed");
			candidates.Add("/var/lib/flatpak/app/dev.zed.Zed/current/active/files/bin/zed");
			candidates.Add("/usr/bin/zeditor");
			candidates.Add("/run/current-system/sw/bin/zeditor");
			candidates.Add("/etc/profiles/per-user/linx/bin/zed");
			candidates.Add("/etc/profiles/per-user/linx/bin/zeditor");
			candidates.Add(IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "zed"));
#endif

			foreach (var candidate in candidates.Distinct())
			{
				if (TryDiscoverInstallation(candidate, out var installation))
					yield return installation;
			}
		}

#if UNITY_EDITOR_LINUX
		[System.Runtime.InteropServices.DllImport ("libc")]
		private static extern int readlink(string path, byte[] buffer, int buflen);

		internal static string GetRealPath(string path)
		{
			byte[] buf = new byte[512];
			int ret = readlink(path, buf, buf.Length);
			if (ret == -1) return path;
			char[] cbuf = new char[512];
			int chars = System.Text.Encoding.Default.GetChars(buf, 0, ret, cbuf, 0);
			return new String(cbuf, 0, chars);
		}
#else
		internal static string GetRealPath(string path)
		{
			return path;
		}
#endif

		public override void CreateExtraFiles(string projectDirectory)
		{
			try
			{
				var zedDirectory = IOPath.Combine(projectDirectory.NormalizePathSeparators(), ".zed");
				Directory.CreateDirectory(zedDirectory);

				var settingsFile = IOPath.Combine(zedDirectory, "settings.json");
				if (File.Exists(settingsFile))
					return;

				File.WriteAllText(settingsFile, DefaultSettingsContent);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}

		public override bool Open(string path, int line, int column, string solution)
		{
			line = Math.Max(1, line);
			column = Math.Max(0, column);

			var directory = IOPath.GetDirectoryName(solution);

			var args = new StringBuilder();
			args.Append($"\"{directory}\"");

			if (!string.IsNullOrEmpty(path))
			{
				args.Append(" -a ");
				args.Append($"\"{path}\"");

				args.Append($":{line}");
				args.Append($":{column}");
			}

			try
			{
				ProcessRunner.Start(ProcessStartInfoFor(Path, args.ToString()));
				return true;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Zed] Error launching editor: {ex}");
				return false;
			}
		}

		private static ProcessStartInfo ProcessStartInfoFor(string application, string arguments)
		{
#if UNITY_EDITOR_OSX
			if (Directory.Exists(application) && application.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
			{
				arguments = $"-n \"{application}\" --args {arguments}";
				application = "open";
				return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect:false, shell: true);
			}
#endif
			return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect: false);
		}

		public static void Initialize()
		{
		}
	}
}
