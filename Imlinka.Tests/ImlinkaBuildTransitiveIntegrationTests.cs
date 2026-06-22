using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Mono.Cecil;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Verifies package-level buildTransitive behavior through real dotnet restore/build commands.
/// </summary>
public sealed class ImlinkaBuildTransitiveIntegrationTests
{
    [Fact]
    public void PackageReference_WhenHostReferencesLocalProject_ShouldWeaveCopiedProjectAssembly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var version = $"{ReadPackageVersion(Path.Combine(repositoryRoot, "Imlinka", "Imlinka.csproj"))}.integration.{Guid.NewGuid():N}";
        var tempRoot = Path.Combine(Path.GetTempPath(), $"Imlinka.BuildTransitive.{Guid.NewGuid():N}");
        var packagesDirectory = Path.Combine(tempRoot, "packages");

        try
        {
            Directory.CreateDirectory(packagesDirectory);
            RunDotnet(
                repositoryRoot,
                "pack",
                "-c",
                "Release",
                Path.Combine(repositoryRoot, "Imlinka", "Imlinka.csproj"),
                "--output",
                packagesDirectory,
                "--no-restore",
                $"/p:PackageVersion={version}",
                "-v:q");

            CreateFixtureProjects(tempRoot, version);

            var hostProject = Path.Combine(tempRoot, "Host", "Host.csproj");
            RunDotnet(
                tempRoot,
                "restore",
                hostProject,
                "--source",
                packagesDirectory,
                "--source",
                "https://api.nuget.org/v3/index.json");
            RunDotnet(tempRoot, "build", hostProject, "-c", "Release", "--no-restore", "-v:m");

            var servicesAssemblyPath = Path.Combine(
                tempRoot,
                "Host",
                "bin",
                "Release",
                "net10.0",
                "Services.dll");

            using var servicesAssembly = AssemblyDefinition.ReadAssembly(servicesAssemblyPath);

            servicesAssembly.MainModule.AssemblyReferences
                .Should()
                .Contain(reference => reference.Name == "Imlinka");

            servicesAssembly.MainModule
                .GetType("Services.Worker")
                .Methods
                .Single(method => method.Name == "Run")
                .Body
                .Instructions
                .Any(instruction =>
                    instruction.Operand is MethodReference
                    {
                        DeclaringType.FullName: "Imlinka.ProjectTracingRuntime",
                        Name: "StartScope"
                    })
                .Should()
                .BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void CreateFixtureProjects(string root, string version)
    {
        var hostDirectory = Path.Combine(root, "Host");
        var servicesDirectory = Path.Combine(root, "Services");
        Directory.CreateDirectory(hostDirectory);
        Directory.CreateDirectory(servicesDirectory);

        File.WriteAllText(
            Path.Combine(hostDirectory, "Host.csproj"),
            $$"""
              <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <OutputType>Exe</OutputType>
                  <TargetFramework>net10.0</TargetFramework>
                  <ImplicitUsings>enable</ImplicitUsings>
                  <Nullable>enable</Nullable>
                </PropertyGroup>
                <ItemGroup>
                  <PackageReference Include="Imlinka" Version="{{version}}" />
                  <ProjectReference Include="..\Services\Services.csproj" />
                </ItemGroup>
              </Project>
              """);

        File.WriteAllText(
            Path.Combine(hostDirectory, "Program.cs"),
            """
            using Services;

            Console.WriteLine(new Worker().Run());
            """);

        File.WriteAllText(
            Path.Combine(servicesDirectory, "Services.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(servicesDirectory, "Worker.cs"),
            """
            namespace Services;

            public sealed class Worker
            {
                public int Run() => 42;
            }
            """);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Imlinka.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static string ReadPackageVersion(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document.Root?
                   .Element("PropertyGroup")?
                   .Element("Version")?
                   .Value
               ?? throw new InvalidOperationException("Imlinka package version was not found.");
    }

    private static CommandResult RunDotnet(string workingDirectory, params string[] arguments)
    {
        var output = new StringBuilder();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} timed out.");
        }

        var result = new CommandResult(process.ExitCode, output.ToString());
        result.ExitCode.Should().Be(0, result.Output);

        return result;
    }

    private sealed record CommandResult(int ExitCode, string Output);
}
