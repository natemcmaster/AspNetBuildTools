// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;

namespace Internal.AspNetCore.Sdk
{
    /// <summary>
    /// Resolves SDKs from the KoreBuild folder using the korebuild-lock.txt file to find the right version of KoreBuild.
    /// </summary>
    public class KoreBuildSdksResolver : SdkResolver
    {
        private static readonly string[] _supportedSdks = new[] { "Internal.AspNetCore.Sdk", "KoreBuild.RepoTasks.Sdk" };

        private static readonly string KoreBuildRootPath = GetKoreBuildRootPath();

        public override string Name => "KoreBuild.Sdks";

        public override int Priority => 5000;

        public override SdkResult Resolve(SdkReference sdkReference, SdkResolverContext resolverContext, SdkResultFactory factory)
        {
            try
            {
                return ResolveImpl(sdkReference, resolverContext, factory);
            }
            catch (Exception ex)
            {
                resolverContext.Logger.LogMessage("KoreBuild SDK resolver encountered unexpected error: " + ex.ToString(), MessageImportance.High);
                return factory.IndicateFailure(null);
            }
        }

        private SdkResult ResolveImpl(SdkReference sdkReference, SdkResolverContext resolverContext, SdkResultFactory factory)
        {
            if (!_supportedSdks.Any(n => sdkReference.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            {
                // let other resolvers run first
                return factory.IndicateFailure(null);
            }

            if (string.IsNullOrEmpty(KoreBuildRootPath))
            {
                return factory.IndicateFailure(new[] { "Could not identify the location of korebuild tools." });
            }

            string korebuildSdksPath;
            var sdkVersion = string.Empty;
            if (Environment.GetEnvironmentVariable("KoreBuildSdksPath") != null)
            {
                // env override
                korebuildSdksPath = Environment.GetEnvironmentVariable("KoreBuildSdksPath");
            }
            else
            {
                var lockFile = TryFindKoreBuildLockFile(resolverContext.ProjectFilePath);
                if (lockFile == null || !lockFile.Exists)
                {
                    return factory.IndicateFailure(new[] { $"Failed to find korebuild-lock.txt for project '{resolverContext.ProjectFilePath}'. This is required to resolve Sdk '{sdkReference.Name}' " });
                }

                var kb = KoreBuildLockFile.Parse(lockFile);

                if (kb.SchemaVersion.HasValue)
                {
                    if (kb.SchemaVersion != 1)
                    {
                        // Out-dated detection. The resolver in Visual Studio may not always be updated.
                        resolverContext.Logger.LogMessage($"{lockFile.FullName}: unsupported schema version: {kb.SchemaVersion}. An update the KoreBuild SDK resolver may be necessary.", MessageImportance.High);
                    }
                }
                else
                {
                    resolverContext.Logger.LogMessage($"{lockFile.FullName}: Missing schema version. Assuming version 1.");
                }

                korebuildSdksPath = Path.Combine(KoreBuildRootPath, kb.Version, "sdks");
                sdkVersion = kb.Version;

                resolverContext.Logger.LogMessage($"Using KoreBuild folder to resolve SDKs: {korebuildSdksPath}", MessageImportance.Low);
            }

            var sdkPath = Path.Combine(korebuildSdksPath, sdkReference.Name);

            return Directory.Exists(sdkPath)
                ? factory.IndicateSuccess(sdkPath, sdkVersion)
                : factory.IndicateFailure(new[] { $"Could not find SDK named '{sdkReference.Name}' in {sdkPath}. Make sure the korebuild-lock.txt file is up to date." });
        }

        private static FileInfo TryFindKoreBuildLockFile(string project)
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(project));
            FileInfo lockFile = null;

            while (dir != null)
            {
                var lockFiles = dir.GetFiles("korebuild-lock.txt");
                if (lockFiles?.Length > 0)
                {
                    lockFile = lockFiles[0];
                    break;
                }
                dir = dir.Parent;
            }

            return lockFile;
        }

        private static string GetKoreBuildRootPath()
        {
            var dotnetHome = Environment.GetEnvironmentVariable("DOTNET_HOME");

            if (string.IsNullOrEmpty(dotnetHome))
            {
                var home = Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetEnvironmentVariable("HOME");
                dotnetHome = Path.Combine(home, ".dotnet");
            }

            return Path.Combine(dotnetHome, "buildtools", "korebuild");
        }
    }
}
