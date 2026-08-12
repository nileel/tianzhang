using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Infrastructure.Persistence;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class ArchitectureBaselineEditorTests
    {
        private static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        [Test]
        public void BehaviorBaselineKeepsOnlyStableInputsStateChangesAndResults()
        {
            string path = Path.Combine(RepositoryRoot, "开发管理", "架构重建行为基线-U-ARCH-REBUILD-01A.txt");
            Assert.That(File.Exists(path), Is.True, "Missing architecture behavior baseline.");
            string text = File.ReadAllText(path);

            string[] requiredMarkers =
            {
                "BASELINE-SLICE: character-creation",
                "BASELINE-SLICE: world-settlement-adventure",
                "BASELINE-SLICE: bounty-combat-return",
                "BASELINE-SLICE: save-restore",
                "BASELINE-SLICE: charter",
                "origin_minor_clan",
                "basic_unarmed",
                "guanzhong_hub",
                "guanzhong_city",
                "bounty_guanzhong_shijiahou",
                "guanzhong_wild",
                "enemy_shijiahou",
                "item_shijia_piece",
                "item_lingshi_low",
                "charter_site_old_water_station",
                "charter_entry_suifu_diji"
            };
            foreach (string marker in requiredMarkers)
                StringAssert.Contains(marker, text, marker);

            StringAssert.DoesNotContain("RectTransform", text);
            StringAssert.DoesNotContain("anchoredPosition", text);
            StringAssert.DoesNotContain("Shader.Find", text);
            StringAssert.DoesNotContain("material.color", text);
        }

        [Test]
        public void TargetBootstrapIsAThinRuntimeCompositionRoot()
        {
            Type type = typeof(GameBootstrap);
            Assert.That(type.IsSealed, Is.True);
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(type), Is.True);

            FieldInfo[] fields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.That(fields, Has.Length.EqualTo(4));
            Assert.That(Array.Exists(fields, field => field.FieldType == typeof(GameBootstrap)), Is.True);
            Assert.That(Array.Exists(fields, field => field.FieldType == typeof(GameRuntime)), Is.True);
            Assert.That(Array.Exists(fields, field => field.FieldType == typeof(GameSaveSlotStore)), Is.True);
            Assert.That(Array.Exists(fields, field => field.FieldType == typeof(string)), Is.True);
        }

        [Test]
        public void MissingBootstrapFailsWithoutCreatingASecondCompositionRoot()
        {
            GameBootstrap existing = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => GameBootstrap.RequireRuntime());

            Assert.AreEqual("game_bootstrap_missing", exception.Message);
            Assert.IsNull(UnityEngine.Object.FindFirstObjectByType<GameBootstrap>());
        }

        [Test]
        public void BoundaryCheckerRejectsDeliberateIllegalDependencies()
        {
            var fixtures = new[]
            {
                new BoundaryFixture(
                    "cycle",
                    "assembly dependency cycle",
                    new AssemblyFixture("Fixture.A", "Fixture.B"),
                    new AssemblyFixture("Fixture.B", "Fixture.A")),
                new BoundaryFixture(
                    "domain-to-feature",
                    "Domain-to-Feature reference is forbidden",
                    new AssemblyFixture("TianZhang.Character", "TianZhang.Features.WorldMap"),
                    new AssemblyFixture("TianZhang.Features.WorldMap")),
                new BoundaryFixture(
                    "sibling-feature",
                    "sibling Feature reference is forbidden",
                    new AssemblyFixture("TianZhang.Features.WorldMap", "TianZhang.Features.Settlement"),
                    new AssemblyFixture("TianZhang.Features.Settlement")),
                new BoundaryFixture(
                    "editor-enters-player",
                    "Editor assembly may not enter Player",
                    new AssemblyFixture("TianZhang.World", "TianZhang.Editor"),
                    new AssemblyFixture("TianZhang.Editor", true))
            };

            foreach (BoundaryFixture fixture in fixtures)
            {
                string fixtureRoot = Path.Combine(Path.GetTempPath(), "tzg-assembly-boundary-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fixtureRoot);
                try
                {
                    foreach (AssemblyFixture assembly in fixture.Assemblies)
                    {
                        string json = BuildAsmdefJson(assembly);
                        File.WriteAllText(Path.Combine(fixtureRoot, assembly.Name + ".asmdef"), json);
                    }

                    ProcessResult result = RunBoundaryChecker(fixtureRoot);
                    Assert.That(result.ExitCode, Is.Not.EqualTo(0), fixture.Name + Environment.NewLine + result.Output);
                    StringAssert.Contains(fixture.ExpectedError, result.Output, fixture.Name);
                }
                finally
                {
                    Directory.Delete(fixtureRoot, true);
                }
            }
        }

        private static ProcessResult RunBoundaryChecker(string assemblyRoot)
        {
            string checker = Path.Combine(RepositoryRoot, "tools", "check-unity-assembly-boundaries.ps1");
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + checker +
                            "\" -RepositoryRoot \"" + RepositoryRoot +
                            "\" -AssemblyRoot \"" + assemblyRoot + "\" -SkipRequiredAssemblies",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (Process process = Process.Start(startInfo))
            {
                Assert.That(process, Is.Not.Null);
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                Assert.That(process.WaitForExit(30000), Is.True, "Boundary checker timed out.");
                return new ProcessResult(process.ExitCode, standardOutput + standardError);
            }
        }

        private static string BuildAsmdefJson(AssemblyFixture fixture)
        {
            var references = new List<string>();
            foreach (string reference in fixture.References)
                references.Add("\"" + reference + "\"");
            string includePlatforms = fixture.EditorOnly ? ",\"includePlatforms\":[\"Editor\"]" : string.Empty;
            return "{\"name\":\"" + fixture.Name + "\",\"references\":[" +
                   string.Join(",", references) + "]" + includePlatforms + "}";
        }

        private readonly struct ProcessResult
        {
            public ProcessResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output;
            }

            public int ExitCode { get; }
            public string Output { get; }
        }

        private sealed class BoundaryFixture
        {
            public BoundaryFixture(string name, string expectedError, params AssemblyFixture[] assemblies)
            {
                Name = name;
                ExpectedError = expectedError;
                Assemblies = assemblies;
            }

            public string Name { get; }
            public string ExpectedError { get; }
            public AssemblyFixture[] Assemblies { get; }
        }

        private sealed class AssemblyFixture
        {
            public AssemblyFixture(string name, params string[] references)
                : this(name, false, references)
            {
            }

            public AssemblyFixture(string name, bool editorOnly, params string[] references)
            {
                Name = name;
                EditorOnly = editorOnly;
                References = references;
            }

            public string Name { get; }
            public bool EditorOnly { get; }
            public string[] References { get; }
        }
    }
}
