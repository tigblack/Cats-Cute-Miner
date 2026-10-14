using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CatMiners.Editor
{
    /// <summary>
    /// Синхронная распаковка .unitypackage (gzip + tar: для каждого GUID — asset, asset.meta, pathname).
    /// AssetDatabase.ImportPackage асинхронный и в batchmode не успевает до выхода; здесь файлы пишутся сразу.
    /// </summary>
    public static class UnityPackageExtractor
    {
        sealed class Entry
        {
            public byte[] Asset;
            public byte[] Meta;
            public string Path;
        }

        /// <returns>Число записанных файлов.</returns>
        public static int Extract(string packagePath, string projectRoot, bool overwrite = false)
        {
            var entries = new Dictionary<string, Entry>();
            using (var fs = File.OpenRead(packagePath))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                var header = new byte[512];
                while (ReadFull(gz, header, 512))
                {
                    if (IsZero(header)) break;
                    string name = ReadString(header, 0, 100);
                    string prefix = ReadString(header, 345, 155);
                    if (!string.IsNullOrEmpty(prefix)) name = prefix + "/" + name;
                    long size = Convert.ToInt64(ReadString(header, 124, 12).Trim().Length == 0 ? "0" : ReadString(header, 124, 12).Trim(), 8);
                    char type = (char)header[156];
                    byte[] data = new byte[size];
                    if (size > 0 && !ReadFull(gz, data, (int)size)) throw new EndOfStreamException("Обрыв архива " + packagePath);
                    long pad = (512 - size % 512) % 512;
                    if (pad > 0) Skip(gz, pad);
                    if (type != '0' && type != '\0') continue;
                    name = name.TrimStart('.', '/');
                    int slash = name.IndexOf('/');
                    if (slash <= 0) continue;
                    string guid = name.Substring(0, slash);
                    string part = name.Substring(slash + 1);
                    if (!entries.TryGetValue(guid, out var e))
                    {
                        e = new Entry();
                        entries[guid] = e;
                    }
                    switch (part)
                    {
                        case "asset": e.Asset = data; break;
                        case "asset.meta": e.Meta = data; break;
                        case "pathname": e.Path = Encoding.UTF8.GetString(data).Split('\n')[0].Trim(); break;
                    }
                }
            }

            int written = 0;
            foreach (var e in entries.Values)
            {
                if (string.IsNullOrEmpty(e.Path) || !e.Path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                string full = Path.Combine(projectRoot, e.Path.Replace('/', Path.DirectorySeparatorChar));
                if (e.Asset != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    if (overwrite || !File.Exists(full))
                    {
                        File.WriteAllBytes(full, e.Asset);
                        written++;
                    }
                }
                else
                {
                    Directory.CreateDirectory(full);
                }
                if (e.Meta != null && (overwrite || !File.Exists(full + ".meta")))
                    File.WriteAllBytes(full + ".meta", e.Meta);
            }
            return written;
        }

        static bool ReadFull(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        static void Skip(Stream s, long count)
        {
            var tmp = new byte[512];
            while (count > 0)
            {
                int n = s.Read(tmp, 0, (int)Math.Min(tmp.Length, count));
                if (n <= 0) return;
                count -= n;
            }
        }

        static bool IsZero(byte[] b)
        {
            foreach (var x in b) if (x != 0) return false;
            return true;
        }

        static string ReadString(byte[] b, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, offset, end - offset);
        }
    }
}
