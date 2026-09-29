using System;
using System.Collections.Generic;
using System.IO;

namespace TcpEcho.Shared
{
    /// <summary>
    /// Minimal "Key = Value" settings reader. Everything after a # is a comment, blank lines
    /// are ignored, and a line without '=' is skipped. Keys are case-insensitive.
    /// </summary>
    public static class IniFile
    {
        public static Dictionary<string, string> Read(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return values;

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0)
                    line = line.Substring(0, hash);

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim();
                if (key.Length > 0)
                    values[key] = line.Substring(eq + 1).Trim();
            }

            return values;
        }
    }
}
