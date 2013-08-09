using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

static class Common
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);

    [DllImport("kernel32.dll")]
    static extern IntPtr LoadLibrary(string dllToLoad);

    public static string CreateMd5Hash(string input)
    {
        using (var md5 = MD5.Create())
        {
            var inputBytes = Encoding.ASCII.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            for (var i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }

    static void CopyTo(Stream source, Stream destination)
    {
        var array = new byte[81920];
        int count;
        while ((count = source.Read(array, 0, array.Length)) != 0)
        {
            destination.Write(array, 0, count);
        }
    }
    
    public static void CreateDirectory(string tempBasePath)
    {
        if (Directory.Exists(tempBasePath))
        {
            try
            {
                Directory.Delete(tempBasePath, true);
                Directory.CreateDirectory(tempBasePath);
            }
            catch
            {
            }
        }
        else
        {
            Directory.CreateDirectory(tempBasePath);
        }
        MoveFileEx(tempBasePath, null, 0x4);
    }

    static byte[] ReadStream(Stream stream)
    {
        var data = new Byte[stream.Length];
        stream.Read(data, 0, data.Length);
        return data;
    }

    public static Assembly ReadExistingAssembly(string name)
    {
        var currentDomain = AppDomain.CurrentDomain;
        var assemblies = currentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            var fullName = assembly.FullName.ToLowerInvariant();
            var indexOf = fullName.IndexOf(',');
            if (indexOf > 1)
            {
                fullName = fullName.Substring(0, indexOf);
            }

            if (fullName == name)
            {
                return assembly;
            }
        }
        return null;
    }

    public static Assembly ReadFromDiskCache(string tempBasePath, string name)
    {
        var assemblyTempFilePath = Path.Combine(tempBasePath, String.Concat(name, ".dll"));
        if (File.Exists(assemblyTempFilePath))
        {
            return Assembly.LoadFile(assemblyTempFilePath);
        }
        assemblyTempFilePath = Path.ChangeExtension(assemblyTempFilePath, "exe");
        if (File.Exists(assemblyTempFilePath))
        {
            return Assembly.LoadFile(assemblyTempFilePath);
        }
        return null;
    }

    public static Assembly ReadFromEmbeddedResources(Dictionary<string, string> assemblyNames, Dictionary<string, string> symbolNames, string name)
    {
        byte[] assemblyData;
        using (var assemblyStream = LoadStream(assemblyNames, name))
        {
            if (assemblyStream == null)
            {
                return null;
            }
            assemblyData = ReadStream(assemblyStream);
        }

        using (var pdbStream = LoadStream(symbolNames, name))
        {
            if (pdbStream != null)
            {
                var pdbData = ReadStream(pdbStream);
                return Assembly.Load(assemblyData, pdbData);
            }
        }

        return Assembly.Load(assemblyData);
    }

    static Stream LoadStream(Dictionary<string, string> resourceNames, string name)
    {
        if (resourceNames.ContainsKey(name))
            return LoadStream(resourceNames[name]);

        return null;
    }

    static Stream LoadStream(string fullname)
    {
        var executingAssembly = Assembly.GetExecutingAssembly();

        if (fullname.EndsWith(".zip"))
        {
            using (var stream = executingAssembly.GetManifestResourceStream(fullname))
            using (var compressStream = new DeflateStream(stream, CompressionMode.Decompress))
            {
                var memStream = new MemoryStream();
                CopyTo(compressStream, memStream);
                memStream.Position = 0;
                return memStream;
            }
        }

        return executingAssembly.GetManifestResourceStream(fullname);
    }

    public static void PreloadUnmanagedLibraries(string tempBasePath, IEnumerable<string> libs)
    {
        // Preload correct library
        var bittyness = IntPtr.Size == 8 ? "64" : "32";

        string name;

        foreach (var lib in libs)
        {
            if (lib.StartsWith(String.Concat("costura", bittyness, ".")))
                name = lib.Substring(10);
            else if (lib.StartsWith("costura."))
                name = lib.Substring(8);
            else
                continue;

            if (name.EndsWith(".zip"))
                name = name.Substring(0, name.Length - 4);

            var assemblyTempFilePath = Path.Combine(tempBasePath, name);

            if (!File.Exists(assemblyTempFilePath))
            {
                using (var copyStream = LoadStream(lib))
                using (var assemblyTempFile = File.OpenWrite(assemblyTempFilePath))
                {
                    CopyTo(copyStream, assemblyTempFile);
                }
            }
        }

        foreach (var lib in libs)
        {
            name = lib.Substring(10);
            if (name.EndsWith(".zip"))
                name = name.Substring(0, name.Length - 4);

            if (name.EndsWith(".dll"))
            {
                var assemblyTempFilePath = Path.Combine(tempBasePath, name);

                LoadLibrary(assemblyTempFilePath);
            }
        }
    }
}