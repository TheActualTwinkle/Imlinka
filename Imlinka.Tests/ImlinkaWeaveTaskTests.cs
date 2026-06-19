using FluentAssertions;
using Imlinka.Build;
using Microsoft.Build.Framework;
using Mono.Cecil;
using IDictionary = System.Collections.IDictionary;
using Xunit;

namespace Imlinka.Tests;

/// <summary>
/// Verifies low-level MSBuild task behavior that is hard to observe through runtime spans.
/// </summary>
public sealed class ImlinkaWeaveTaskTests
{
    [Fact]
    public void Execute_WhenAssemblyIsSigned_ShouldSkipWithWarning()
    {
        var assemblyPath = Path.Combine(Path.GetTempPath(), $"Imlinka.Signed.{Guid.NewGuid():N}.dll");

        try
        {
            CreateSignedAssemblyReferencingImlinka(assemblyPath);
            var buildEngine = new TestBuildEngine();
            var task = new ImlinkaWeaveTask
            {
                BuildEngine = buildEngine,
                AssemblyPath = assemblyPath
            };

            task.Execute().Should().BeTrue();
            buildEngine.Warnings.Should().ContainSingle(message => message.Contains("signed assembly", StringComparison.OrdinalIgnoreCase));
            buildEngine.Errors.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(assemblyPath))
                File.Delete(assemblyPath);
        }
    }

    private static void CreateSignedAssemblyReferencingImlinka(string assemblyPath)
    {
        var name = new AssemblyNameDefinition("Imlinka.SignedFixture", new Version(1, 0, 0, 0))
        {
            HasPublicKey = true,
            PublicKey = [0, 36, 0, 0, 4, 128, 0, 0, 148, 0, 0, 0]
        };

        var assembly = AssemblyDefinition.CreateAssembly(name, "Imlinka.SignedFixture", ModuleKind.Dll);
        assembly.MainModule.AssemblyReferences.Add(new AssemblyNameReference("Imlinka", new Version(0, 1, 5, 0)));
        assembly.Write(assemblyPath);
    }

    private sealed class TestBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];

        public List<string> Warnings { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs) =>
            throw new NotSupportedException();

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e)
        {
            Errors.Add(e.Message ?? string.Empty);
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
            Warnings.Add(e.Message ?? string.Empty);
        }
    }
}
