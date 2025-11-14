using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Serilog;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static Nuke.Common.EnvironmentInfo;
using Nuke.Common.Tools.MSBuild;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Microsoft.Build.Tasks;

//class Build : NukeBuild
//{
//    [Solution] readonly Solution Solution;
//    [Parameter("Configuration to build - Default is 'Debug' (same as Visual Studio)")]
//    readonly string Configuration = IsLocalBuild ? "Debug R23" : "Release R23";

//    Target Compile => _ => _
//        .Description("Builds the solution exactly like Visual Studio")
//        .Executes(() =>
//        {
//            MSBuildTasks.MSBuild(s => s
//                .SetProjectFile(Solution)
//                .SetConfiguration(Configuration)
//                .SetVerbosity(MSBuildVerbosity.Minimal)
//                .SetTargets("Build")
//                .SetMaxCpuCount(Environment.ProcessorCount)
//                .DisableNodeReuse()
//            );
//        });
//    public static int Main() => Execute<Build>(x => x.Compile);
//}
class Build : NukeBuild
{
    /// <summary>
    /// This is the entry point - when you run "nuke" it runs this target by default
    /// </summary>
    public static int Main() => Execute<Build>(x => x.Compile);

    // ===== PARAMETERS =====
    // These are the inputs to your build

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)", Name = "config")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Revit versions to build for", Name = "rv")]
    readonly string[] RevitVersions = { "R22", "R23", "R24", "R25", "R26" };

    [Solution]
    readonly Solution Solution;

    // ===== DIRECTORY DEFINITIONS =====
    // Define where files go

    // RootDirectory is defined by Nuke and is the .sln directory
    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath InstallerDirectory => RootDirectory / "installer";

    // ===== TARGETS =====
    // These are the tasks you can run

    readonly string[] Net48Configs = new[] { "Debug R21", "Debug R22", "Debug R23", "Debug R24" };
    readonly string[] Net8Configs = new[] { "Debug R25", "Debug R26" };

    readonly string[] Net48Frameworks = new[] { "net48" };
    readonly string[] Net8Frameworks = new[] { "net8.0-windows" };

    readonly (string framework, string[] configs)[] Frameworks = new[]
    {
    ("net48", new[] { "Debug R21", "Debug R22", "Debug R23", "Debug R24" }),
    ("net8.0-windows", new[] { "Debug R25", "Debug R26" })
};

    readonly string[] TargetFrameworks = new[] { "net48", "net8.0-windows" };
    string GetTargetFrameworkForRevitVersion(string revitVersion) =>
    revitVersion switch
    {
        "R21" or "R22" or "R23" or "R24" => "net48",
        "R25" or "R26" => "net8.0-windows",
        _ => throw new Exception($"Unknown revit version: {revitVersion}")
    };



    string GetConfigurationForTargetFramework(string targetFramework) =>
    targetFramework switch
    {
        "net48" => "Debug R22", // Default for net48
        "net8.0-windows" => "Debug R25", // Default for net8
        _ => throw new Exception($"Unknown target framework: {targetFramework}")
    };

    string GetBaseIntermediateOutputPath(string targetFramework) => $"obj\\{targetFramework}\\";
    // Cannot handle (treats as seperate calls) spaces in path (like "Debug R22"), so only use target framework and revit version
    string GetIntermediateOutputPath(string targetFramework, string revitversion) =>
        GetBaseIntermediateOutputPath(targetFramework) + revitversion + "\\";
    string GetBaseOutputPath(string targetFramework) => $"bin\\{targetFramework}\\";

    Target Clean => _ => _
    .Before(Restore)
    .Executes(() =>
    {
        Project[] excludedProjects =
        [
            Solution.GetProject("_build"),
        ];

        CleanDirectory(ArtifactsDirectory);
        CleanDirectory(OutputDirectory);
        foreach (var project in Solution.AllProjects)
        {
            if (excludedProjects.Contains(project)) continue;

            CleanDirectory(project.Directory / "bin");
            CleanDirectory(project.Directory / "obj");
        }

        // Build desired folder structure based of Directory.Build.targets
        foreach (var revitversion in RevitVersions)
        {
            string config = $"{Configuration} {revitversion}";
            string tf = GetTargetFrameworkForRevitVersion(revitversion);
            string baseIntermediateOutputPath = GetBaseIntermediateOutputPath(tf);
            string intermediateOutputPath = GetIntermediateOutputPath(tf, revitversion);
            string baseOutputPath = GetBaseOutputPath(tf);

            DotNetClean(settings => settings
                .SetProject(Solution)
                .SetFramework(tf)
                .SetProperty("BaseIntermediateOutputPath", baseIntermediateOutputPath)
                .SetProperty("BaseOutputPath", baseOutputPath)
                ////.SetProperty("IntermediateOutputPath", intermediateOutputPath)
                //.SetProperty("AppendTargetFrameworkToIntermediateOutputPath", "false")
                .SetProperty("AppendTargetFrameworkToOutputPath", "false")
                //.SetProperty("AppendRuntimeIdentifierToOutputPath", "false")
                //.SetProperty("AppendRuntimeIdentifierToIntermediateOutputPath", "false")
                .SetConfiguration(config) // Configuration doesn't matter for cleaning
                .SetVerbosity(DotNetVerbosity.minimal)
                .EnableNoLogo());
        }
    });




    // Can potentially speed up builds by restoring only twice for each framework
    Target Restore => _ => _
        .Description("Restores NuGet packages")
        .DependsOn(Clean)
        .Executes(() =>
        {
            // Clean each project for each Revit version configuration
            foreach (var revitversion in RevitVersions)
            {
                string config = $"{Configuration} {revitversion}";
                string tf = GetTargetFrameworkForRevitVersion(revitversion);
                string baseIntermediateOutputPath = GetBaseIntermediateOutputPath(tf);
                string intermediateOutputPath = GetIntermediateOutputPath(tf, revitversion);
                string baseOutputPath = GetBaseOutputPath(tf);

                DotNetRestore(s => s
                    .SetProjectFile(Solution)
                    .AddProperty("Configuration", config)
                    .SetProperty("TargetFramework", tf)
                    .SetProperty("BaseIntermediateOutputPath", baseIntermediateOutputPath)
                                    .SetProperty("BaseOutputPath", baseOutputPath)
                // //.SetProperty("IntermediateOutputPath", intermediateOutputPath)
                // .SetProperty("AppendTargetFrameworkToIntermediateOutputPath", "false")
                .SetProperty("AppendTargetFrameworkToOutputPath", "false")
                //.SetProperty("AppendRuntimeIdentifierToOutputPath", "false")
                //.SetProperty("AppendRuntimeIdentifierToIntermediateOutputPath", "false")
                    .SetVerbosity(DotNetVerbosity.minimal));

            }
        });



    Target Compile => _ => _
    .Description("Compiles the solution for all Revit versions")
    .DependsOn(Restore)
    .Executes(() =>
    {
        foreach (var revitVersion in RevitVersions)
        {
            var config = $"{Configuration} {revitVersion}";
            string tf = GetTargetFrameworkForRevitVersion(revitVersion);
            string baseIntermediateOutputPath = GetBaseIntermediateOutputPath(tf);
            string intermediateOutputPath = GetIntermediateOutputPath(tf, revitVersion);
            string baseOutputPath = GetBaseOutputPath(tf);
            Serilog.Log.Information($"Building for {revitVersion}...");

            MSBuildTasks.MSBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(config)
                                //.SetVerbosity(DotNetVerbosity.minimal)
                                //.SetFramework(tf)
                                .SetProperty("BaseIntermediateOutputPath", baseIntermediateOutputPath)
                                .SetProperty("BaseOutputPath", baseOutputPath)
                                ////.SetProperty("IntermediateOutputPath", intermediateOutputPath)
                                //.SetProperty("AppendTargetFrameworkToIntermediateOutputPath", "false")
                                .SetProperty("AppendTargetFrameworkToOutputPath", "false")
                                //.SetProperty("AppendRuntimeIdentifierToOutputPath", "false")
                                //.SetProperty("AppendRuntimeIdentifierToIntermediateOutputPath", "false")
                                //.EnableNoRestore() // Already restored above
                                .SetVerbosity(MSBuildVerbosity.Minimal)
                                .SetTargets("Build")
                                .SetMaxCpuCount(Environment.ProcessorCount)
                                .DisableNodeReuse());
                //.EnableNoLogo());
        }
    });

    //Target Package => _ => _
    //    .Description("Packages build outputs for each Revit version")
    //    .DependsOn(Compile)
    //    .Executes(() =>
    //    {
    //        foreach (var revitVersion in RevitVersions)
    //        {
    //            var config = $"{Configuration}{revitVersion}";
    //            var packageDir = ArtifactsDirectory / revitVersion;

    //            EnsureCleanDirectory(packageDir);

    //            // Copy DLLs (adjust paths based on your actual output structure)
    //            var binPath = RootDirectory / "RevitTools.Addin" / "bin" / config;

    //            CopyFileToDirectory(binPath / "RevitTools.Addin.dll", packageDir);
    //            CopyFileToDirectory(binPath / "RevitTools.Shared.dll", packageDir);
    //            CopyFileToDirectory(binPath / "RevitApiWrapper.dll", packageDir);

    //            // Copy any other dependencies that aren't Revit API DLLs
    //            foreach (var dll in Directory.GetFiles(binPath, "*.dll")
    //                .Where(f => !Path.GetFileName(f).StartsWith("Revit") &&
    //                            !Path.GetFileName(f).StartsWith("AdWindows")))
    //            {
    //                CopyFileToDirectory(dll, packageDir);
    //            }

    //            // Copy .addin manifest
    //            var manifestSource = RootDirectory / "RevitTools.Addin" / "Manifests" / "RevTools.addin";
    //            CopyFileToDirectory(manifestSource, packageDir);

    //            // Copy resources (icons, etc.)
    //            var resourcesSource = RootDirectory / "RevitTools.Addin" / "Resources";
    //            if (Directory.Exists(resourcesSource))
    //            {
    //                CopyDirectoryRecursively(resourcesSource, packageDir / "Resources");
    //            }

    //            Serilog.Log.Information($"Packaged {revitVersion} to {packageDir}");
    //        }
    //    });

    //Target BuildInstaller => _ => _
    //    .Description("Builds MSI installer using WiX")
    //    .DependsOn(Package)
    //    .Executes(() =>
    //    {
    //        // We'll implement this after setting up WiX
    //        Serilog.Log.Information("Installer target will be implemented with WiX");
    //    });


    /// <summary>
    ///     Clean and log the specified directory.
    /// </summary>
    static void CleanDirectory(AbsolutePath path)
    {
        Serilog.Log.Information("Cleaning directory: {Directory}", path);
        path.CreateOrCleanDirectory();
    }

}



// NOTES
// IntermediateOutputPath etc does not seem to take effect reliably if set in Directory.Build.props|targets
//  so we set them explicitly in each command here. Also, cannot have spaces in these paths when set this way.
// BaseIntermediateOutputPath & BaseOutputPath must match (with obj and bin respectively) otherwise you get issues with folder 
//  structure in obj (ie obj/net48, net8, Debug R23)
// DotNetBuild does not handle .Net Framework projects (net48) but will build .Net 5+ projects. This could be due to the fact we are 
//   using sdk 9 in the _build project and globals.json which may not handle net48 but possible downgrade to sdk 8 may work. For now 
//   msBuild is used as it seems to handle both frameworks fine.
// If cannot find a solution then build for one .net framework at a time 