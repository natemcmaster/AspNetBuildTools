// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;

namespace NuGet.Tasks.ProjectModel
{
    internal class ProjectFrameworkInfo
    {
        public ProjectFrameworkInfo(
            string buildTargetPath,
            NuGetFramework targetFramework,
            IEnumerable<PackageReferenceInfo> dependencies)
            : this(buildTargetPath, targetFramework, dependencies.ToDictionary(i => i.Id, i => i, StringComparer.OrdinalIgnoreCase))
        { }

        public ProjectFrameworkInfo(
            string buildTargetPath,
            NuGetFramework targetFramework,
            IReadOnlyDictionary<string, PackageReferenceInfo> dependencies)
        {
            TargetFramework = targetFramework ?? throw new ArgumentNullException(nameof(targetFramework));
            Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            BuildTargetPath = buildTargetPath ?? throw new System.ArgumentNullException(nameof(buildTargetPath));
        }

        public string BuildTargetPath { get; }

        public NuGetFramework TargetFramework { get; }

        public IReadOnlyDictionary<string, PackageReferenceInfo> Dependencies { get; }
    }
}
