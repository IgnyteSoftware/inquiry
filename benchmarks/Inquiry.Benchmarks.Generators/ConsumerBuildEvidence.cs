using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Benchmarks.Generators;

internal static class ConsumerBuildEvidence
{
    public static async Task RunAsync(string destination)
    {
        var root = Path.GetFullPath(destination);
        if (Directory.Exists(root) || File.Exists(root))
            throw new ArgumentException("Use a new output directory. Existing evidence is never overwritten.");
        Directory.CreateDirectory(root);
        await RunDotnetAsync(root, "--info");
        var samples = new List<object>();
        var framework = "net" + Environment.Version.Major + ".0";
        var references = new[]
        {
            typeof(Inquiry.Entities.InquiryTableAttribute).Assembly.Location,
            typeof(Inquiry.Sqlite.DependencyInjection.SqliteInquiryServiceCollectionExtensions).Assembly.Location,
            typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location
        };
        var analyzer = typeof(Inquiry.Sqlite.Analyzer.InquirySqliteGenerator).Assembly.Location;
        var shared = Path.Combine(Path.GetDirectoryName(analyzer)!, "Inquiry.Generators.Shared.dll");
        foreach (var count in new[] { 25, 100, 500 })
        {
            var fixture = new GeneratorFixture(count);
            for (var repetition = 1; repetition <= 3; repetition++)
            {
                var directory = Path.Combine(root, $"entities-{count}-sample-{repetition}");
                Directory.CreateDirectory(directory);
                var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup",
                        new XElement("TargetFramework", framework),
                        new XElement("AssemblyName", "GeneratorConsumer"),
                        new XElement("Nullable", "enable"),
                        new XElement("LangVersion", "12"),
                        new XElement("UseSharedCompilation", "false"),
                        new XElement("EmitCompilerGeneratedFiles", "true"),
                        new XElement("CompilerGeneratedFilesOutputPath", "obj/generated")),
                    new XElement("ItemGroup",
                        references.Select(path => new XElement("Reference", new XAttribute("Include", Path.GetFileNameWithoutExtension(path)), new XElement("HintPath", path))),
                        new XElement("Analyzer", new XAttribute("Include", analyzer)),
                        new XElement("Analyzer", new XAttribute("Include", shared))));
                await InitializeAsync(directory, project, fixture.Sources);
                var controlDirectory = directory + "-without-analyzer";
                Directory.CreateDirectory(controlDirectory);
                var controlProject = new XElement(project);
                controlProject.Descendants("Analyzer").Remove();
                var controlSources = new Dictionary<string, string>(fixture.Sources);
                var generated = fixture.CreateDriver().RunGenerators(fixture.Original);
                fixture.Verify(generated, fixture.Original);
                foreach (var source in generated.GetRunResult().Results.SelectMany(result => result.GeneratedSources))
                    controlSources.Add(source.HintName, source.SourceText.ToString());
                await InitializeAsync(controlDirectory, controlProject, controlSources);
                var controlElapsed = await RunDotnetAsync(controlDirectory, "build", "-c", "Release", "--no-restore", "--verbosity", "quiet");
                samples.Add(new { EntityCount = count, Repetition = repetition, Scenario = "emitted-source-control", ElapsedMilliseconds = controlElapsed, OutputCount = GeneratorFixture.Snapshot(generated).Length });
                await MeasureAsync("clean-build", fixture.Original);
                await MeasureAsync("unrelated-edit", fixture.Unrelated);
                await MeasureAsync("model-edit", fixture.Model);
                await MeasureAsync("relation-filter-edit", fixture.RelationFilter);

                async Task MeasureAsync(string scenario, Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
                {
                    // Reset each edit to the same baseline. This build is outside the measurement.
                    if (scenario != "clean-build")
                    {
                        foreach (var source in fixture.Sources)
                            await File.WriteAllTextAsync(Path.Combine(directory, source.Key), source.Value);
                        await RunDotnetAsync(directory, "build", "-c", "Release", "--no-restore", "--verbosity", "quiet");
                        foreach (var tree in compilation.SyntaxTrees)
                            if (tree.ToString() != fixture.Sources[tree.FilePath])
                                await File.WriteAllTextAsync(Path.Combine(directory, tree.FilePath), tree.ToString());
                    }
                    var elapsed = await RunDotnetAsync(directory, "build", "-c", "Release", "--no-restore", "--verbosity", "quiet");
                    var expected = fixture.CreateDriver().RunGenerators(compilation);
                    fixture.Verify(expected, compilation);
                    var actual = Directory.GetFiles(Path.Combine(directory, "obj", "generated"), "*.cs", SearchOption.AllDirectories)
                        .Select(path => Path.GetFileName(path) + "\n" + File.ReadAllText(path)).Order(StringComparer.Ordinal).ToArray();
                    if (!actual.SequenceEqual(GeneratorFixture.Snapshot(expected)))
                    {
                        await File.WriteAllTextAsync(Path.Combine(directory, "expected-output.json"), JsonSerializer.Serialize(GeneratorFixture.Snapshot(expected)));
                        await File.WriteAllTextAsync(Path.Combine(directory, "actual-output.json"), JsonSerializer.Serialize(actual));
                        throw new InvalidOperationException($"Compiler-generated output differs from fresh-driver output: {scenario}, {count} entities.");
                    }
                    samples.Add(new { EntityCount = count, Repetition = repetition, Scenario = scenario, ElapsedMilliseconds = elapsed, OutputCount = actual.Length });
                    Console.WriteLine($"{count} entities, sample {repetition}, {scenario}: {elapsed:F2} ms, {actual.Length} outputs.");
                }
            }
        }
        var report = new
        {
            Disposition = "Diagnostic only; release thresholds and representative consumer scale require approval.",
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            SdkInfo = await File.ReadAllTextAsync(Path.Combine(root, "build.log")),
            Assets = references.Append(analyzer).Append(shared).Select(path => new { Path = path, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }),
            Samples = samples
        };
        await File.WriteAllTextAsync(Path.Combine(root, "build-evidence.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task InitializeAsync(string directory, XElement project, Dictionary<string, string> sources)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "Consumer.csproj"), project.ToString());
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            await File.WriteAllTextAsync(Path.Combine(directory, name), "<Project />");
        foreach (var source in sources)
            await File.WriteAllTextAsync(Path.Combine(directory, source.Key), source.Value);
        await RunDotnetAsync(directory, "restore");
    }

    private static async Task<double> RunDotnetAsync(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        stopwatch.Stop();
        var log = await output + await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(log);
        await File.AppendAllTextAsync(Path.Combine(directory, "build.log"), string.Join(" ", arguments) + Environment.NewLine + log);
        return stopwatch.Elapsed.TotalMilliseconds;
    }
}
