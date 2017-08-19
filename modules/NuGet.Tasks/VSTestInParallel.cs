// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using NuGet.Frameworks;
using NuGet.Tasks.Policies;
using NuGet.Tasks.ProjectModel;
using NuGet.Tasks.Utilties;

namespace NuGet.Tasks
{
    /// <summary>
    /// Applies policies to a repository on how NuGet can be used, such as restricting which PackageReference's are allowed,
    /// which versions of packages can be used, which feeds are available, etc.
    /// </summary>
    public class VSTestInParallel : Microsoft.Build.Utilities.Task, ICancelableTask
    {
        private readonly CancellationTokenSource _cts;

        public VSTestInParallel()
        {
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// The MSBuild projects or solutions to which the policy should be applied.
        /// </summary>
        [Required]
        public ITaskItem[] Projects { get; set; }

        /// <summary>
        /// Key-value base list of properties to be applied to <see cref="Projects" /> during project evaluation.
        /// e.g. "Configuration=Debug;BuildNumber=1234"
        /// </summary>
        public string ProjectProperties { get; set; }

        public void Cancel()
        {
            _cts.Cancel();
        }

        public override bool Execute()
        {
            if (_cts.IsCancellationRequested)
            {
                return false;
            }

            var projects = CreateProjectContext();

            if (_cts.IsCancellationRequested)
            {
                return false;
            }

            var testGroups = projects.SelectMany(f => f.Frameworks).GroupBy(f => f.TargetFramework);

            foreach (var group in testGroups)
            {
                if (!group.Any())
                {
                    continue;
                }

                var tfm = group.Key.GetDotNetFrameworkName(DefaultFrameworkNameProvider.Instance);
                var args = new List<string>
                {
                    "vstest",
                    "--Framework:" + tfm,
                    "--Parallel",
                };

                if (group.Key.IsDesktop())
                {
                    args.Add("--TestAdapterPath:" + Path.GetDirectoryName(group.First().BuildTargetPath));
                }

                args.AddRange(group.Select(p => p.BuildTargetPath));
                const string sep = "\n   ";
                var list = string.Join(sep, args);
                var dotnet = DotNetMuxer.MuxerPathOrDefault();

                Log.LogMessage(MessageImportance.High, $"Testing {tfm}...");
                Log.LogMessage(MessageImportance.Normal, $"Test args: {sep}{dotnet}{sep}{list}");

                var process = new Process
                {
                    StartInfo =
                    {
                        FileName = dotnet,
                        Arguments = ArgumentEscaper.EscapeAndConcatenate(args),
                    }
                };

                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    return false;
                }
            }

            return !Log.HasLoggedErrors;
        }

        internal IReadOnlyList<ProjectInfo> CreateProjectContext()
        {
            var solutionProps = MSBuildListSplitter.GetNamedProperties(ProjectProperties);
            var projectFiles = Projects.SelectMany(p => GetFilePaths(p, solutionProps)).Distinct();
            var projects = new ConcurrentBag<ProjectInfo>();
            var stop = Stopwatch.StartNew();

            Parallel.ForEach(projectFiles, projectFile =>
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    projects.Add(ProjectInfoFactory.Create(projectFile));
                }
                catch (Exception ex)
                {
                    Log.LogErrorFromException(ex);
                    Cancel();
                }
            });

            stop.Stop();
            Log.LogMessage(MessageImportance.Low, $"Finished design-time build in {stop.ElapsedMilliseconds}ms");
            return projects.ToArray();
        }

        private IEnumerable<string> GetFilePaths(ITaskItem projectOrSolution, IDictionary<string, string> solutionProperties)
        {
            var projectFilePath = projectOrSolution.ItemSpec.Replace('\\', '/');

            if (Path.GetExtension(projectFilePath).Equals(".sln", StringComparison.OrdinalIgnoreCase))
            {
                // prefer the AdditionalProperties metadata as this is what the MSBuild task will use when building solutions
                var props = MSBuildListSplitter.GetNamedProperties(projectOrSolution.GetMetadata("AdditionalProperties"));
                props.TryGetValue("Configuration", out var config);

                if (config == null)
                {
                    solutionProperties.TryGetValue("Configuration", out config);
                }

                var sln = SolutionInfoFactory.Create(projectFilePath, config);
                foreach (var project in sln.Projects)
                {
                    yield return project;
                }
            }
            else
            {
                yield return Path.GetFullPath(projectFilePath);
            }
        }
    }
}
