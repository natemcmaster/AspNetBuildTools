KoreBuild.SdkResolver
=====================

This is a custom implementation of Microsoft.Build.Framework.SdkResolver that will find MSBuild SDKs
from the KoreBuild folder ($HOME/.dotnet/buildtools/korebuild). Its purpose is to allows MSBuild
to resolve <Sdk> nodes to targets that ship in KoreBuild.

It uses korebuild-lock.txt to find the appropriate KoreBuild version on resolve the targets from that project.

#### Example usage

```xml
<Project>
    <!-- this ships by default in Visual Studio -->
    <Sdk Name="Microsoft.NET.Sdk" />

    <!-- this is bundled in KoreBuild-->
    <Sdk Name="KoreBuild.RepoTasks.Sdk" />

    <PropertyGroup>
        <TargetFramework>netstandard2.0</TargetFramework>
    </PropertyGroup>
</Project>
```

The SDK resolver will use the korebuild-lock.txt file to find
