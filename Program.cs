using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace vftablechecker
{
    class Program
    {
        private static Dictionary<string, (string vftableptr, List<string> functionsptr)> lines = new Dictionary<string, (string, List<string>)>();

        private class SearchDef
        {
            public (string name, uint offset)[] alternativeVFTables;
            public (string name, List<(string unityVersion, uint offset)>[] platforms)[] methods;

            public SearchDef((string name, uint offset)[] alternativeVFTables, params string[] methods)
            {
                this.alternativeVFTables = alternativeVFTables;
                this.methods = new (string, List<(string, uint)>[])[methods.Length];
                for (int i = 0; i < this.methods.Length; ++i)
                {
                    this.methods[i].name = methods[i];
                    this.methods[i].platforms = new List<(string, uint)>[archs.Length];
                    for (int j = 0; j < archs.Length; ++j)
                        this.methods[i].platforms[j] = new List<(string, uint)>();
                }
            }
        }

        private class SearchOffsets
        {
            public (uint, uint) vftableOffset;
            public (uint, uint)[] methodoffsets;

            public SearchOffsets(int size)
            {
                this.methodoffsets = new (uint, uint)[size];
            }
        }

        internal class UnityVersionComparer : IComparer<string>
        {
            public int Compare(string left, string right)
            {
                int[] leftparts = left.Split(Path.DirectorySeparatorChar).Last().Split('.').Select(s => int.Parse(s)).ToArray();
                int[] rightparts = right.Split(Path.DirectorySeparatorChar).Last().Split('.').Select(s => int.Parse(s)).ToArray();
                long leftsum = leftparts[0] * 10000 + leftparts[1] * 100 + leftparts[2];
                long rightsum = rightparts[0] * 10000 + rightparts[1] * 100 + rightparts[2];

                if (leftsum > rightsum)
                    return 1;
                if (leftsum < rightsum)
                    return -1;
                return 0;
            }
        }

        internal class UnityVersionOffsetComparer : IComparer<(string uv, uint offset)>
        {
            UnityVersionComparer uvc = new UnityVersionComparer();

            public int Compare((string uv, uint offset) left, (string uv, uint offset) right)
            {
                return uvc.Compare(left.uv, right.uv);
            }
        }






        public static readonly (string name, string[] flags, (string[] unityminversions, (string dll, string pdb) paths)[] paths)[] archs = new[]
        {
            /*
            ("mono x86 nondev", new [] {
                (new [] { "2017.2.0", "2018.1.0" }, "win32_nondevelopment_mono/UnityPlayer.dll"),
                (new string[0], "win32_nondevelopment_mono/player_win.exe")
            }),
            */
            ("mono x64 nondev", new [] { "Mono", "X64" }, new [] {
                (new [] { "2018.3.0", "2019.1.0" }, ("win64_nondevelopment_mono/UnityPlayer.dll", "win64_nondevelopment_mono/UnityPlayer_Win64_mono_x64.pdb")),
                (new [] { "2018.2.0" }            , ("win64_nondevelopment_mono/UnityPlayer.dll", "win64_nondevelopment_mono/UnityPlayer_Win32_x64_mono.pdb")),
                (new [] { "2017.2.0" }            , ("win64_nondevelopment_mono/UnityPlayer.dll", "win64_nondevelopment_mono/UnityPlayer_Win32_x64.pdb")),
                (new [] { "2017.1.0" }            , ("win64_nondevelopment_mono/UnityPlayer.dll", "win64_nondevelopment_mono/UnityPlayer_Win32_x64_mono.pdb")),
                (new string[0]                    , ("win64_nondevelopment_mono/player_win.exe" , "win64_nondevelopment_mono/player_win.pdb"))
            }),

            ("il2cpp x64 nondev", new [] { "Il2Cpp", "X64" }, new [] {
                (new [] { "2018.3.0", "2019.1.0" }, ("win64_nondevelopment_il2cpp/UnityPlayer.dll", "win64_nondevelopment_il2cpp/UnityPlayer_Win64_il2cpp_x64.pdb")),
                (new [] { "2018.2.0" }            , ("win64_nondevelopment_il2cpp/UnityPlayer.dll", "win64_nondevelopment_il2cpp/UnityPlayer_Win32_x64_il2cpp.pdb")),
                (new [] { "2017.2.0" }            , ("win64_nondevelopment_il2cpp/UnityPlayer.dll", "win64_nondevelopment_il2cpp/UnityPlayer_Win32_x64.pdb")),
                (new [] { "2017.1.0" }            , ("win64_nondevelopment_il2cpp/UnityPlayer.dll", "win64_nondevelopment_il2cpp/UnityPlayer_Win32_x64_il2cpp.pdb")),
                (new string[0]                    , ("win64_nondevelopment_il2cpp/player_win.exe" , "win64_nondevelopment_il2cpp/player_win.pdb"))
            }),

            /*
            ("il2cpp x86 nondev", new [] {
                (new [] { "2017.2.0", "2018.1.0" }, "win32_nondevelopment_il2cpp/UnityPlayer.dll"),
                (new string[0], "win32_nondevelopment_il2cpp/player_win.exe")
            }),
            */
            /*
            ("il2cpp x64 nondev", new [] {
                (new [] { "2017.2.0", "2018.1.0" }, "win64_nondevelopment_il2cpp/UnityPlayer.dll"),
                (new string[0], "win64_nondevelopment_il2cpp/player_win.exe")
            }),
            */

            //("il2cpp x64 dev", "win64_development_il2cpp/UnityPlayer.dll"),
            //("il2cpp x86 dev", "win32_development_il2cpp/UnityPlayer.dll"),
            //("mono x64 dev", "win64_development_mono/UnityPlayer.dll"),
            //("mono x86 dev", "win32_development_mono/UnityPlayer.dll"),
        };

        /*
        private static Dictionary<string, SearchDef> searchs = new Dictionary<string, SearchDef> {
            { "GfxDeviceClient", new SearchDef("SendVRDeviceEvent") },
            { "VRDevice", new SearchDef("BeforeRendering", "Update", "GetActiveEyeTexture", "ResolveColorAndDepthToEyeTextures", "AfterRendering", "PostPresent", "GetEyeTextureDimension", "GetProjectionMatrix") }
        };
        */
        private static Dictionary<string, SearchDef> searchs = new Dictionary<string, SearchDef>();

        private static string sourceDirectory;



        //private static List<(string[] unityversion, (string name, (string name, ulong offset)[] methods)[] vftables)> vftableouts = new List<(string[] unityversion, (string name, (string name, ulong offset)[] methods)[] vftables)>();
        // vftable > method > unityversion, offset
        private static Dictionary<string, Dictionary<string, Dictionary<string, ulong>>> vftableouts2 = new Dictionary<string, Dictionary<string, Dictionary<string, ulong>>>();



        private static Dictionary<string, (string name, ulong offset)[]> lastvftables;

        static void Main(string[] args)
        {

            LoadConfig();


            if (args.Length > 0)
            {
                string version = args[0];
                string rootpath = sourceDirectory + "/" + version;

                for (int iArch = 0; iArch < archs.Length; ++iArch)
                {
                    var arch = archs[iArch];

                    var paths = arch.paths.First(v => IsUnityVersionOverOrEqual(version, v.unityminversions)).paths;
                    string dllpath = rootpath + "/" + paths.dll;
                    string pdbpath = rootpath + "/" + paths.pdb;
                    if (!File.Exists(dllpath) || !File.Exists(pdbpath))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(version + ": Missing required files for arch " + arch.name);
                        Console.WriteLine("    " + dllpath + ": " + (File.Exists(dllpath) ? "Found" : "Not Found"));
                        Console.WriteLine("    " + pdbpath + ": " + (File.Exists(pdbpath) ? "Found" : "Not Found"));
                        Console.ResetColor();
                        continue;
                    }
                    Console.WriteLine(version + " " + arch.name);

                    CheckDump(iArch, version, pdbpath, dllpath);
                }

                Console.WriteLine();
                foreach (KeyValuePair<string, (string name, ulong offset)[]> vftable in lastvftables)
                {
                    Console.WriteLine($"internal static class {vftable.Key}VFTable");
                    Console.WriteLine("{");

                    foreach (var vftableentry in vftable.Value)
                    {
                        Console.WriteLine($"    uint {vftableentry.name} = {vftableentry.offset};");
                    }

                    Console.WriteLine("}");
                }
                Console.WriteLine();
            }
            else
            {
                UnityVersionComparer comparer = new UnityVersionComparer();

                List<string> directories = Directory.GetDirectories(sourceDirectory).Where(d => !d.Contains("_tmp")).ToList();
                directories.Sort(comparer);

                LoadCache();

                // Check versions

                for (int iArch = 0; iArch < archs.Length; ++iArch)
                {
                    var arch = archs[iArch];

                    foreach (string rootpath in directories)
                    {
                        string version = rootpath.Split(Path.DirectorySeparatorChar).Last();


                        var paths = arch.paths.First(v => IsUnityVersionOverOrEqual(version, v.unityminversions)).paths;
                        string dllpath = rootpath + "/" + paths.dll;
                        string pdbpath = rootpath + "/" + paths.pdb;
                        if (!File.Exists(dllpath) || !File.Exists(pdbpath))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(version + ": Missing required files for arch " + arch.name);
                            Console.WriteLine("    " + dllpath + ": " + (File.Exists(dllpath) ? "Found" : "Not Found"));
                            Console.WriteLine("    " + pdbpath + ": " + (File.Exists(pdbpath) ? "Found" : "Not Found"));
                            Console.ResetColor();
                            continue;
                        }

                        // Check if we already have every values

                        bool needsCheck = false;
                        foreach (var search in searchs.Values)
                        {
                            foreach (var method in search.methods)
                            {
                                bool hasValidResult = false;
                                foreach (var result in method.platforms[iArch])
                                {
                                    if (result.unityVersion == version)
                                    {
                                        hasValidResult = result.offset != 0;
                                        if (!hasValidResult)
                                            break;
                                    }
                                }
                                if (!hasValidResult)
                                {
                                    Console.WriteLine("missing " + method.name);
                                    needsCheck = true;
                                    break;
                                }
                            }
                            if (needsCheck)
                                break;
                        }
                        if (needsCheck)
                        {
                            Console.WriteLine(version + " " + arch.name);

                            CheckDump(iArch, version, pdbpath, dllpath);
                        }
                        else
                            Console.WriteLine(version + " " + arch.name + " - DISCARTED");
                    }
                }

                SaveToCache();

                // Sort results

                UnityVersionOffsetComparer uvoc = new UnityVersionOffsetComparer();
                foreach (KeyValuePair<string, SearchDef> search in searchs)
                    foreach (var method in search.Value.methods)
                        foreach (var platform in method.platforms)
                            platform.Sort(uvoc);

                // Final output

                foreach (KeyValuePair<string, SearchDef> search in searchs)
                {
                    Console.WriteLine($"internal static class {search.Key}VFTable");
                    Console.WriteLine("{");

                    bool firstMethod = true;
                    foreach (var method in search.Value.methods)
                    {
                        if (firstMethod)
                            firstMethod = false;
                        else
                            Console.WriteLine("    ");

                        Console.WriteLine($"    #region {method.name}_vftableoffset values");

                        for (int iPlatform = 0; iPlatform < method.platforms.Length; ++iPlatform)
                        {
                            var results = method.platforms[iPlatform];
                            string flags = string.Join(" | ", archs[iPlatform].flags.Select(flag => "NativeSignatureFlags." + flag));

                            Console.WriteLine("    // " + archs[iPlatform].name);

                            List<string> lineCache = new List<string>();

                            uint lastOffset = results[0].offset;
                            string lastUV = results[0].unityVersion;
                            List<string> lastUVs = new List<string>();
                            lastUVs.Add(lastUV);
                            for (int i = 1; i < results.Count; ++i)
                            {
                                var result = results[i];
                                if (result.offset != lastOffset)
                                {
                                    lineCache.Add($"    [NativeFieldValue({flags}, {lastOffset}, {string.Join(", ", lastUVs.Select(uv => $"\"{uv}\""))})]");
                                    lastOffset = result.offset;
                                    lastUVs.Clear();
                                    lastUVs.Add(lastUV = result.unityVersion);
                                }
                                else if (!IsUnityVersionOverOrEqual(result.unityVersion, lastUV))
                                {
                                    lastUV = result.unityVersion;
                                    lastUVs.Add(lastUV);
                                }
                            }
                            Console.WriteLine($"    [NativeFieldValue({flags}, {lastOffset}, {string.Join(", ", lastUVs)})]");
                            for (int i = lineCache.Count - 1; i >= 0; --i)
                                Console.WriteLine(lineCache[i]);
                        }

                        Console.WriteLine("    #endregion");
                        Console.WriteLine($"    public static uint {method.name};");
                    }

                    Console.WriteLine("}");
                }
            }

        }

        private static void LoadConfig()
        {
            JObject config = JsonConvert.DeserializeObject<JObject>(File.ReadAllText("config.json"));
            sourceDirectory = config["sourceDirectory"].ToString();
            foreach (var searchEntry in (JObject)config["searchs"])
            {
                List<(string, uint)> searchAlts = new List<(string, uint)>();
                foreach (var alt in ((JObject)searchEntry.Value)["alternativeVFTables"])
                {
                    JArray altData = alt as JArray;
                    searchAlts.Add((altData[0].ToString(), altData[1].ToObject<uint>()));
                }
                List<string> searchMethods = new List<string>();
                foreach (var method in ((JObject)searchEntry.Value)["methods"])
                    searchMethods.Add(method.ToString());
                searchs.Add(searchEntry.Key, new SearchDef(searchAlts.ToArray(), searchMethods.ToArray()));
            }

        }

        private static void LoadCache()
        {
            if (!File.Exists("cache.json"))
            {
                Console.WriteLine("No cache found.");
                return;
            }

            try
            {
                var cache = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, uint>>>>>(File.ReadAllText("cache.json"));

                foreach (var vftables in searchs)
                    if (cache.TryGetValue(vftables.Key, out var methodsCache))
                        foreach (var searchMethod in vftables.Value.methods)
                            if (methodsCache.TryGetValue(searchMethod.name, out var platformsCache))
                                foreach (var platformCache in platformsCache)
                                {
                                    List<(string, uint)> results = new List<(string, uint)>();
                                    foreach (var offsetCache in platformCache.Value)
                                        if (offsetCache.Value != 0)
                                            results.Add((offsetCache.Key, offsetCache.Value));

                                    for (int iArch = 0; iArch < archs.Length; ++iArch)
                                    {
                                        if (archs[iArch].name == platformCache.Key)
                                        {
                                            searchMethod.platforms[iArch] = results;
                                            break;
                                        }
                                    }

                                }
            }
            catch
            {
                Console.Error.WriteLine("Failed to load cache. Discarding.");
            }
        }

        private static void SaveToCache()
        {
            Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, uint>>>> cache = new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, uint>>>>();
            foreach (var entry in searchs)
            {
                Dictionary<string, Dictionary<string, Dictionary<string, uint>>> methodCache = new Dictionary<string, Dictionary<string, Dictionary<string, uint>>>();

                foreach (var method in entry.Value.methods)
                {
                    Dictionary<string, Dictionary<string, uint>> platformCache = new Dictionary<string, Dictionary<string, uint>>();

                    for (int iPlatform = 0; iPlatform < method.platforms.Length; ++iPlatform)
                    {
                        var platform = method.platforms[iPlatform];
                        Dictionary<string, uint> offsetCache = new Dictionary<string, uint>();
                        foreach (var offset in platform)
                            offsetCache.Add(offset.unityVersion, offset.offset);

                        platformCache.Add(archs[iPlatform].name, offsetCache);
                    }

                    methodCache.Add(method.name, platformCache);
                }

                cache.Add(entry.Key, methodCache);
            }

            File.WriteAllText("cache.json", JsonConvert.SerializeObject(cache, Formatting.Indented));
        }

        private static bool IsUnityVersionOverOrEqual(string currentversion, string[] validversions)
        {
            if (validversions == null || validversions.Length == 0)
                return true;

            string[] versionparts = currentversion.Split('.');

            foreach (string validversion in validversions)
            {
                string[] validversionparts = validversion.Split('.');

                if (
                    int.Parse(versionparts[0]) >= int.Parse(validversionparts[0]) &&
                    int.Parse(versionparts[1]) >= int.Parse(validversionparts[1]) &&
                    int.Parse(versionparts[2]) >= int.Parse(validversionparts[2]))
                    return true;
            }

            return false;
        }

        private static bool IsUnityVersionOverOrEqual(string currentversion, string validversion)
        {
            string[] versionparts = currentversion.Split('.');

            string[] validversionparts = validversion.Split('.');

            if (
                int.Parse(versionparts[0]) >= int.Parse(validversionparts[0]) &&
                int.Parse(versionparts[1]) >= int.Parse(validversionparts[1]) &&
                int.Parse(versionparts[2]) >= int.Parse(validversionparts[2]))
                return true;

            return false;
        }



        private static unsafe void CheckDump(int iArch, string unityversion, string pdbfile, string dllfile)
        {
            Dictionary<string, SearchOffsets> searchoffsets = new Dictionary<string, SearchOffsets>();
            foreach (KeyValuePair<string, SearchDef> entry in searchs)
                searchoffsets[entry.Key] = new SearchOffsets(entry.Value.methods.Length);
            
            (SearchOffsets searchoffsets, int methodIndex) nextSize = (null, 0);

            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = @"llvm-pdbutil";
            startInfo.Arguments = $"dump -publics \"{pdbfile}\"";
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            process.StartInfo = startInfo;

            if (!process.Start())
                Console.WriteLine("Failed to start program" + process.ExitCode);
            string line;
            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                //Console.WriteLine("[llvm-pdbutil] " + line);
                if (nextSize != (null, 0))
                {
                    string[] addr = line.Split('=')[2].Trim().Split(':');
                    (uint, uint) addrParsed = (uint.Parse(addr[0]), uint.Parse(addr[1]));
                    if (nextSize.methodIndex == -1)
                        nextSize.searchoffsets.vftableOffset = addrParsed;
                    else
                        nextSize.searchoffsets.methodoffsets[nextSize.methodIndex] = addrParsed;

                    nextSize = (null, 0);
                    continue;
                }

                if (!line.Contains("`"))
                    continue;

                foreach (KeyValuePair<string, SearchDef> entry in searchs)
                {
                    if (line.Contains($"{entry.Key}@"))
                    {
                        string split = line.Split('`')[1];
                        if (split == $"??_7{entry.Key}@@6B@" || entry.Value.alternativeVFTables.Any(alt => split == $"??_7{entry.Key}@@6B{alt.name}@@@"))
                            nextSize = (searchoffsets[entry.Key], -1);
                        else
                        {
                            var methods = entry.Value.methods;
                            for (int iMethod = 0; iMethod < methods.Length; ++iMethod)
                            {
                                if (split.StartsWith($"?{methods[iMethod].name}@{entry.Key}@@"))
                                {
                                    nextSize = (searchoffsets[entry.Key], iMethod);
                                    break;
                                }
                            }
                        }
                        continue;
                    }
                }
            }
            process.WaitForExit();

            byte[] dlldata = File.ReadAllBytes(dllfile);
            fixed (byte* dataPtr = dlldata)
            {
                ImageNtHeaders* imageNtHeaders = AnalyseModuleWin((IntPtr)dataPtr);
                ImageSectionHeader* pSech = ImageFirstSection(imageNtHeaders);

                Dictionary<string, (string name, ulong offset)[]> vftables = new Dictionary<string, (string name, ulong offset)[]>();

                foreach (KeyValuePair<string, SearchDef> searchdef in searchs)
                {
                    vftables[searchdef.Key] = new (string name, ulong offset)[searchdef.Value.methods.Length];

                    (uint section, uint rva) vftableoffset = searchoffsets[searchdef.Key].vftableOffset;

                    if (vftableoffset.section == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"{searchdef.Key + ":",-40} F:0x{0:X7} / R:0x{0:X9}");
                        Console.ResetColor();

                        for (int i = 0; i < searchdef.Value.methods.Length; ++i)
                        {
                            searchdef.Value.methods[i].platforms[iArch].Add((unityversion, 0));
                            /*
                            if (!searchdef.Value.methodsOffsets[i].TryGetValue(0, out List<string> unityversions))
                                searchdef.Value.methodsOffsets[i][0] = unityversions = new List<string>();
                            unityversions.Add(unityversion);
                            */
                        }

                        continue;
                    }

                    ulong runtimeoffsetvftable = Rva2OffsetRuntime(vftableoffset.rva, vftableoffset.section, pSech, imageNtHeaders);
                    uint fileoffsetvftable = Rva2OffsetFile(vftableoffset.rva, vftableoffset.section, pSech, imageNtHeaders);
                    //Console.WriteLine($"\n{searchdef.Key + ":",-40} F:0x{fileoffsetvftable:X7} / R:0x{runtimeoffsetvftable:X9}");

                    ulong[] methodruntimeoffset = new ulong[searchoffsets[searchdef.Key].methodoffsets.Length];
                    uint[] methodvftableoffset = new uint[searchoffsets[searchdef.Key].methodoffsets.Length];

                    int remaining = 0;
                    for (int i = 0; i < searchoffsets[searchdef.Key].methodoffsets.Length; ++i)
                    {
                        if (searchoffsets[searchdef.Key].methodoffsets[i].Item1 == 0)
                            continue;

                        (uint section, uint rva) methodoffset = searchoffsets[searchdef.Key].methodoffsets[i];
                        methodruntimeoffset[i] = Rva2OffsetRuntime(methodoffset.rva, methodoffset.section, pSech, imageNtHeaders);

                        ++remaining;
                    }

                    ulong* methodptr = (ulong*)((ulong)dataPtr + fileoffsetvftable);
                    uint offset = 0;
                    while (remaining > 0)
                    {
                        //Console.WriteLine($"{offset + ":",-3} 0x{*(methodptr + offset):X9}");
                        for (int i = 0; i < methodruntimeoffset.Length; ++i)
                        {
                            if (0x180000000UL > *(methodptr + offset) || *(methodptr + offset) > 0x190000000UL)
                                throw new Exception($"The target function pointer is outside of the file! Value: 0x{*(methodptr + offset):X}");
                            if (methodvftableoffset[i] != 0 || methodruntimeoffset[i] == 0)
                                continue;
                            if (methodruntimeoffset[i] == *(methodptr + offset))
                            {
                                //Console.WriteLine($"{searchdef.Value.methods[i]} @ vftable+{offset * 8}");
                                methodvftableoffset[i] = offset;
                                --remaining;
                                break;
                            }
                        }
                        offset++;
                    }

                    for (int i = 0; i < searchdef.Value.methods.Length; ++i)
                    {
                        (uint section, uint rva) methodoffset = searchoffsets[searchdef.Key].methodoffsets[i];
                        var results = searchdef.Value.methods[i].platforms[iArch];

                        // Remove old result
                        for (int iResult = 0; iResult < results.Count; ++iResult)
                        {
                            if (results[iResult].unityVersion == unityversion)
                            {
                                results.RemoveAt(iResult);
                                break;
                            }
                        }

                        if (methodoffset.section > 0)
                        {
                            vftables[searchdef.Key][i] = (searchdef.Value.methods[i].name, methodvftableoffset[i] * 8);
                            results.Add((unityversion, methodvftableoffset[i] * 8));
                        }
                        else
                        {
                            vftables[searchdef.Key][i] = (searchdef.Value.methods[i].name, 0);
                            searchdef.Value.methods[i].platforms[iArch].Add((unityversion, 0));
                        }
                    }
                }

                bool equivalent = lastvftables != null;
                if (equivalent)
                {
                    foreach (KeyValuePair<string, (string name, ulong offset)[]> vftable in vftables)
                    {
                        for (int i = 0; i < vftable.Value.Length; ++i)
                        {
                            if (vftable.Value[i] != lastvftables[vftable.Key][i])
                            {
                                equivalent = false;
                                break;
                            }
                        }
                    }
                }

                if (!equivalent)
                {
                    if (lastvftables != null)
                    {
                        Console.WriteLine();
                        foreach (KeyValuePair<string, (string name, ulong offset)[]> vftable in vftables)
                        {
                            Console.WriteLine($"internal static class {vftable.Key}VFTable");
                            Console.WriteLine("{");

                            foreach (var vftableentry in vftable.Value)
                            {
                                Console.WriteLine($"    uint {vftableentry.name} = {vftableentry.offset};");
                            }

                            Console.WriteLine("}");
                        }
                        Console.WriteLine();
                    }
                    lastvftables = vftables;
                }

                // Rva2Offset(_, pSech, imageNtHeaders)

                // TODO read file and check matching function offsets
            }
        }

        internal static unsafe ImageNtHeaders* AnalyseModuleWin(IntPtr moduleBaseAddress)
        {
            if (*(byte*)(moduleBaseAddress + 0x0) != 0x4D || *(byte*)(moduleBaseAddress + 0x1) != 0x5A)
                throw new ArgumentException("The passed module isn't a valid PE file");

            int OFFSET_TO_PE_HEADER_OFFSET = 0x3c;
            uint offsetToPESig = *(uint*)(moduleBaseAddress + OFFSET_TO_PE_HEADER_OFFSET);
            IntPtr pPESig = IntPtr.Add(moduleBaseAddress, (int)offsetToPESig);


            if (*(byte*)(pPESig + 0x0) != 0x50 || *(byte*)(pPESig + 0x1) != 0x45 || *(byte*)(pPESig + 0x2) != 0x0 || *(byte*)(pPESig + 0x3) != 0x0)
                throw new ArgumentException("The passed module isn't a valid PE file");

            return (ImageNtHeaders*)pPESig;
        }



        private static unsafe ImageSectionHeader* ImageFirstSection(ImageNtHeaders* ntheader)
        {
            return (ImageSectionHeader*)((ulong)ntheader + 24 + ntheader->fileHeader.sizeOfOptionalHeader);
        }

        private static unsafe uint Rva2OffsetFile(uint rva, uint section, ImageSectionHeader* psh, ImageNtHeaders* pnt)
        {
            if (rva == 0)
                return 0;
            ImageSectionHeader* pshI = psh + section - 1;
            return (uint)(rva + pshI->pointerToRawData);
        }

        private static unsafe ulong Rva2OffsetRuntime(uint rva, uint section, ImageSectionHeader* psh, ImageNtHeaders* pnt)
        {
            if (rva == 0)
                return 0;
            ImageSectionHeader* pshI = psh + section - 1;
            return (uint)(rva + pshI->virtualAddress) + 0x180000000UL;
        }
    }
}
