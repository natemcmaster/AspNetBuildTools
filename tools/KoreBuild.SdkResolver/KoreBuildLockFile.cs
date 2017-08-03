// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;

namespace Internal.AspNetCore.Sdk
{
    internal class KoreBuildLockFile
    {
        public static KoreBuildLockFile Parse(FileInfo file)
        {
            var kb = new KoreBuildLockFile();
            foreach (var line in File.ReadAllLines(file.FullName))
            {
                var splitIdx = line.IndexOf(':');
                if (splitIdx <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, splitIdx).Trim();
                var value = line.Substring(splitIdx + 1).Trim();

                switch (key.ToLowerInvariant())
                {
                    case "version":
                        kb.Version = value;
                        break;
                    case "commithash":
                        kb.CommitHash = value;
                        break;
                    case "schema":
                        if (int.TryParse(value, out var schemaVersion))
                        {
                            kb.SchemaVersion = schemaVersion;
                        }
                        break;
                }
            }

            return kb;
        }

        public int? SchemaVersion { get; private set; }
        public string Version { get; private set; }
        public string CommitHash { get; private set; }
    }
}
