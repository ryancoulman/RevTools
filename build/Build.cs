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
using System.IO;
using System.Linq;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    /// <summary>
    /// This is the entry point - when you run "nuke" it runs this target by default
    /// </summary>
    public static int Main () => Execute<Build>(x => x.Compile);

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


    Target Clean => _ => _
        .Description("Cleans output and artifacts directories")
        .Before(Restore)
        .Executes(() =>
        {
            Project[] excludedProjects =
                [
                    Solution.GetProject("_build")
                ];

            // Delete old build outputs
            CleanDirectory(OutputDirectory);
            CleanDirectory(ArtifactsDirectory);

            foreach (var project in Solution.AllProjects)
            {
                if (excludedProjects.Contains(project)) continue;

                CleanDirectory(project.Directory / "bin");
                CleanDirectory(project.Directory / "obj");
            }


            // Clean each Revit version configuration
            foreach (var revitVersion in RevitVersions)
            {
                var config = $"{Configuration} {revitVersion}";
                Serilog.Log.Information($"Building for {revitVersion} with config: {config}");

                DotNetClean(s => s
                    .SetProject(Solution)
                    .SetConfiguration(config)
                    .SetVerbosity(DotNetVerbosity.minimal)
                    .EnableNoLogo());
            }
        });

    Target Restore => _ => _
        .Description("Restores NuGet packages")
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .Description("Compiles the solution for all Revit versions")
        .DependsOn(Clean, Restore)
        .Executes(() =>
        {
            // Build for each Revit version
            foreach (var revitVersion in RevitVersions)
            {
                var config = $"{Configuration} {revitVersion}";

                Serilog.Log.Information($"Building for {revitVersion}...");

                DotNetBuild(s => s
                    .SetProjectFile(Solution)
                    .SetConfiguration(config)
                    .SetVerbosity(DotNetVerbosity.minimal));
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
        Log.Information("Cleaning directory: {Directory}", path);
        path.CreateOrCleanDirectory();
    }

}
