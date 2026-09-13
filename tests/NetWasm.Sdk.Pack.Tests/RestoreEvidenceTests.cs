using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class RestoreEvidenceTests
{
    [Fact]
    public void ReaderParsesAuthoritativeTargetsAndDependencyEdges()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        var lockFile = Path.Combine(directory.Path, "packages.lock.json");
        File.WriteAllText(assets, "{\"targets\":{\"NetWasm,Version=v0.1\":{\"Producer/1.0.0\":{\"type\":\"project\",\"dependencies\":{\"Dependency\":\"[1.0.0]\"}},\"Dependency/1.0.0\":{\"type\":\"package\"}}}}");
        File.WriteAllText(lockFile, "{\"version\":1}");
        var evidence = new RestoreEvidence(
            assets,
            Hash(assets),
            lockFile,
            Hash(lockFile),
            ["NetWasm,Version=v0.1"],
            "7.6.0",
            "10.0.302")
        {
            Required = true
        };
        var parsed = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence);
        Assert.Single(parsed.Graph);
        Assert.Equal("Dependency", parsed.Graph[0].Id);
        Assert.Equal("[1.0.0]", parsed.Graph[0].VersionRange);
        Assert.Equal("1.0.0", parsed.Graph[0].ResolvedVersion);
        Assert.Equal("Producer/1.0.0", parsed.Graph[0].Source);
    }

    [Fact]
    public void ReaderRejectsChangedAssetsTargetMismatchAndMalformedGraph()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        File.WriteAllText(assets, "{\"targets\":{\"net10.0\":{}}}");
        var baseEvidence = new RestoreEvidence(assets, Hash(assets), null, null, ["NetWasm,Version=v0.1"], "", "") { Required = true };
        Assert.Equal(NetWasmPackErrorCode.NWPK005, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(baseEvidence)).Code);
        var malformed = Path.Combine(directory.Path, "malformed.json");
        File.WriteAllText(malformed, "not json");
        var malformedEvidence = baseEvidence with { AssetsFilePath = malformed, AssetsFileHash = Hash(malformed), TargetKeys = ["net10.0"] };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(malformedEvidence)).Code);
    }

    [Fact]
    public void ValidatorRejectsIncompleteDuplicateAndPrivatePublicEvidence()
    {
        var validator = new RestoreEvidenceValidator();
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(new RestoreEvidence("assets", "", null, null, [], "", "") { Required = true })).Code);
        var duplicate = new RestoreEvidence("assets", "hash", null, null, ["target"], "", "")
        {
            Graph =
            [
                new RestoreDependencyEvidence("A", "1.0.0", "target", false, false),
                new RestoreDependencyEvidence("A", "1.0.0", "target", false, false)
            ]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => validator.Validate(duplicate)).Code);
        var privateEdge = duplicate with { Graph = [new RestoreDependencyEvidence("A", "bad", "target", true, false)] };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => validator.Validate(privateEdge)).Code);
        var invalidRange = duplicate with { Graph = [new RestoreDependencyEvidence("A", "1.0.0", "target", false, false) { VersionRange = "not-a-range" }] };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => validator.Validate(invalidRange)).Code);
    }

    [Fact]
    public void ValidatorAndReaderAllowUnspecifiedOptionalEvidence()
    {
        var unspecified = RestoreEvidence.Unspecified;
        new RestoreEvidenceValidator().Validate(unspecified);
        Assert.Same(unspecified, new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(unspecified));
        Assert.Throws<ArgumentNullException>(() => new FileRestoreEvidenceReader(null!));
    }

    [Fact]
    public void ValidatorTreatsSamePackageFromDifferentParentsAsDistinctEdges()
    {
        var evidence = new RestoreEvidence("assets", "hash", null, null, ["target"], "", "")
        {
            Graph =
            [
                new RestoreDependencyEvidence("Shared", "1.0.0", "target", false, false) { Source = "Project/1.0.0" },
                new RestoreDependencyEvidence("Shared", "1.0.0", "target", false, false) { Source = "Transit/1.0.0" }
            ]
        };
        new RestoreEvidenceValidator().Validate(evidence);
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() =>
            new RestoreEvidenceValidator().Validate(new RestoreEvidence("", "hash", null, null, ["target"], "", "") { Required = true })).Code);
    }

    [Fact]
    public void ValidatorRejectsMissingLockHashAndAmbiguousTargets()
    {
        var validator = new RestoreEvidenceValidator();
        var missingLockHash = new RestoreEvidence("assets", "hash", "packages.lock.json", "", ["target"], "", "") { Required = true };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Validate(missingLockHash)).Code);
        var missingTargets = new RestoreEvidence("assets", "hash", null, null, [], "", "") { Required = true };
        Assert.Equal(NetWasmPackErrorCode.NWPK005, Assert.Throws<NetWasmPackException>(() => validator.Validate(missingTargets)).Code);
        var duplicateTargets = missingTargets with { TargetKeys = ["target", "target"] };
        Assert.Equal(NetWasmPackErrorCode.NWPK005, Assert.Throws<NetWasmPackException>(() => validator.Validate(duplicateTargets)).Code);
    }

    [Fact]
    public void ReaderRetainsProjectPrivateAndDevelopmentMetadataAndChecksLockNodes()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        var lockFile = Path.Combine(directory.Path, "packages.lock.json");
        File.WriteAllText(assets, """
        {
          "project": {
            "frameworks": {
              "NetWasm,Version=v0.1": {
                "dependencies": {
                  "Private.Dependency": { "version": "[1.0.0]", "privateAssets": "all" },
                  "Dev.Dependency": { "version": "[2.0.0]", "developmentDependency": true }
                }
              }
            }
          },
          "targets": {
            "NetWasm,Version=v0.1": {
              "Producer/1.0.0": {
                "type": "project",
                "dependencies": {
                  "Private.Dependency": "[1.0.0]",
                  "Dev.Dependency": "[2.0.0]",
                  "Transit.Dependency": "[3.0.0]"
                }
              },
              "Private.Dependency/1.0.0": { "type": "package" },
              "Dev.Dependency/2.0.0": { "type": "package" },
              "Transit.Dependency/3.0.0": { "type": "package" }
            }
          }
        }
        """);
        File.WriteAllText(lockFile, """
        {
          "version": 2,
          "dependencies": {
            "NetWasm,Version=v0.1": {
              "Private.Dependency": { "type": "Direct", "requested": "[1.0.0]", "resolved": "1.0.0" },
              "Dev.Dependency": { "type": "Direct", "requested": "[2.0.0]", "resolved": "2.0.0" },
              "Transit.Dependency": { "type": "Transitive", "resolved": "3.0.0" }
            }
          }
        }
        """);
        var evidence = new RestoreEvidence(
            assets,
            Hash(assets),
            lockFile,
            Hash(lockFile),
            ["NetWasm,Version=v0.1"],
            "7.6.0",
            "10.0.302")
        {
            Required = true
        };

        var graph = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence).Graph;
        Assert.Contains(graph, edge => edge.Id == "Private.Dependency" && edge.IsPrivate);
        Assert.Contains(graph, edge => edge.Id == "Dev.Dependency" && edge.IsDevelopmentDependency);
        Assert.Contains(graph, edge => edge.Id == "Transit.Dependency" && !edge.IsPrivate && !edge.IsDevelopmentDependency);

        File.WriteAllText(lockFile, """
        { "version": 2, "dependencies": { "NetWasm,Version=v0.1": {
          "Missing.Dependency": { "type": "Transitive", "resolved": "9.0.0" }
        } } }
        """);
        var stale = evidence with { LockFileHash = Hash(lockFile) };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(stale)).Code);
    }

    [Fact]
    public void RequestBuilderRejectsPrivateRestoreEdgeEvenWhenDeclaredDependencyLooksPublic()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        File.WriteAllText(assets, """
        {
          "project": { "frameworks": { "NetWasm,Version=v0.1": {
            "dependencies": { "Dependency": { "version": "[1.0.0]", "privateAssets": "all" } }
          } } },
          "targets": { "NetWasm,Version=v0.1": {
            "Producer/1.0.0": { "type": "project", "dependencies": { "Dependency": "[1.0.0]" } },
            "Dependency/1.0.0": { "type": "package" }
          } }
        }
        """);
        var inputs = PackTestFixtures.Inputs(dependencies: [new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)]) with
        {
            Restore = new RestoreEvidence(assets, Hash(assets), null, null, [CanonicalPackPlanBuilder.CanonicalTargetFramework], "", "") { Required = true }
        };

        var exception = Assert.Throws<NetWasmPackException>(() => PackTestFixtures.Builder().Build(inputs));
        Assert.Equal(NetWasmPackErrorCode.NWPK007, exception.Code);
    }

    [Fact]
    public void RequestBuilderMapsProjectTargetAliasToCanonicalDependencyGroup()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        File.WriteAllText(assets, """
        { "targets": { "netwasm0.1": {
          "Producer/1.0.0": { "type": "project", "dependencies": { "Dependency": "[1.0.0]" } },
          "Dependency/1.0.0": { "type": "package" }
        } } }
        """);
        var inputs = PackTestFixtures.Inputs(dependencies:
        [new CanonicalPackageDependencyInput("Dependency", "[1.0.0]", CanonicalPackPlanBuilder.CanonicalTargetFramework)]) with
        {
            Restore = new RestoreEvidence(assets, Hash(assets), null, null, ["netwasm0.1"], "", "") { Required = true }
        };

        var package = PackTestFixtures.Builder().Build(inputs);
        Assert.Equal(CanonicalPackPlanBuilder.CanonicalTargetFramework, package.Restore.TargetKeys.Single());
        Assert.Equal(CanonicalPackPlanBuilder.CanonicalTargetFramework, package.Restore.Graph.Single().TargetFramework);
    }

    [Fact]
    public void ReaderRejectsLockAndAssetsShapeErrorsAndHashMismatches()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        var lockFile = Path.Combine(directory.Path, "packages.lock.json");
        File.WriteAllText(assets, "{\"targets\":{\"target\":{\"A/1.0.0\":{\"type\":\"package\"}}}}");
        File.WriteAllText(lockFile, "{\"dependencies\":{\"target\":[]}}");
        var baseEvidence = new RestoreEvidence(assets, Hash(assets), lockFile, Hash(lockFile), ["target"], "", "") { Required = true };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(baseEvidence)).Code);

        File.WriteAllText(lockFile, "{\"dependencies\":{\"target\":{\"A\":{}}}}");
        var unresolved = baseEvidence with { LockFileHash = Hash(lockFile) };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(unresolved)).Code);

        File.WriteAllText(lockFile, "{\"dependencies\":{\"target\":{\"A\":{\"resolved\":\"1.0.0\"},\"a\":{\"resolved\":\"1.0.0\"}}}}");
        var duplicate = unresolved with { LockFileHash = Hash(lockFile) };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(duplicate)).Code);

        File.WriteAllText(lockFile, "{\"dependencies\":{\"target\":{\"A\":{\"resolved\":\"1.0.0\"}}}}");
        File.WriteAllText(assets, "{\"targets\":{\"target\":{}}}");
        var absentAsset = duplicate with { AssetsFileHash = Hash(assets), LockFileHash = Hash(lockFile) };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(absentAsset)).Code);
    }

    [Fact]
    public void ReaderParsesVersionObjectsMetadataFallbackAndRejectsMalformedDependencies()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        File.WriteAllText(assets, """
        {
          "project": { "frameworks": {
            "other": { "dependencies": { "Dependency": { "version": "[1.0.0]" } } },
            "target": { "dependencies": { "Dependency": { "version": "[1.0.0]", "privateAssets": "all", "developmentDependency": true }, "NoVersion": {} } }
          } },
          "targets": { "target": {
            "Producer/1.0.0": { "type": "project", "dependencies": {
              "Dependency": { "version": "[1.0.0]" }, "MissingNode": "2.0.0", "NoVersion": "" }
            },
            "Dependency/1.0.0": { "type": "package" }
          } }
        }
        """);
        var evidence = new RestoreEvidence(assets, Hash(assets), null, null, ["target"], "", "") { Required = true };
        var error = Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence));
        Assert.Equal(NetWasmPackErrorCode.NWPK006, error.Code);

        File.WriteAllText(assets, "{\"targets\":{\"target\":{\"Producer/1.0.0\":{\"type\":\"project\",\"dependencies\":{\"MissingNode\":\"2.0.0\"}}}}}");
        var parsed = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence with { AssetsFileHash = Hash(assets) });
        Assert.Null(parsed.Graph.Single().ResolvedVersion);
        Assert.False(parsed.Graph.Single().IsPrivate);
    }

    [Fact]
    public void ReaderIncludesDirectProjectPackageDependenciesInRestoreGraph()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        File.WriteAllText(assets, """
        {
          "project": { "frameworks": {
            "target": { "dependencies": { "Dependency": { "version": "[1.0.0]" }, "Unresolved": "[2.0.0]" } }
          } },
          "targets": { "target": {
            "Dependency/1.0.0": { "type": "package" }
          } }
        }
        """);

        var evidence = new RestoreEvidence(assets, Hash(assets), null, null, ["target"], "", "") { Required = true };
        var graph = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence).Graph;
        var dependency = graph.Single(edge => edge.Id == "Dependency");

        Assert.Equal("Dependency", dependency.Id);
        Assert.Equal("[1.0.0]", dependency.VersionRange);
        Assert.Equal("1.0.0", dependency.ResolvedVersion);
        Assert.Equal("project", dependency.Source);
        var unresolved = graph.Single(edge => edge.Id == "Unresolved");
        Assert.Null(unresolved.ResolvedVersion);
    }

    [Fact]
    public void ReaderIncludesProjectReferenceDependenciesFromDependencyGroups()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        File.WriteAllText(assets, """
        {
          "project": { "frameworks": {
            "target": { "dependencies": { "Other.Project": { "version": "[2.0.0]" } } }
          } },
          "projectFileDependencyGroups": {
            "target": ["Project.Dependency >= 1.0.0", "Other.Project [2.0.0]", "Package.Dependency [3.0.0]"]
          },
          "targets": { "target": {
            "Project.Dependency/1.0.0": { "type": "project" },
            "Other.Project/2.0.0": { "type": "project" },
            "Package.Dependency/3.0.0": { "type": "package" }
          } }
        }
        """);

        var evidence = new RestoreEvidence(assets, Hash(assets), null, null, ["target"], "", "") { Required = true };
        var graph = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence).Graph;

        var minimum = graph.Single(edge => edge.Id == "Project.Dependency");
        Assert.Equal("[1.0.0, )", minimum.VersionRange);
        Assert.Equal("1.0.0", minimum.ResolvedVersion);
        Assert.Equal("project", minimum.Source);
        Assert.Equal("[2.0.0]", graph.Single(edge => edge.Id == "Other.Project").VersionRange);
        Assert.DoesNotContain(graph, edge => edge.Id == "Package.Dependency");
    }

    [Fact]
    public void ReaderParsesProjectReferencesWithoutProjectFrameworkMetadata()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        File.WriteAllText(assets, """
        {
          "projectFileDependencyGroups": { "target": ["Project.Dependency >= 1.0.0"] },
          "targets": { "target": { "Project.Dependency/1.0.0": { "type": "project" } } }
        }
        """);

        var evidence = new RestoreEvidence(assets, Hash(assets), null, null, ["target"], "", "") { Required = true };
        var dependency = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence).Graph.Single();
        Assert.Equal("Project.Dependency", dependency.Id);
        Assert.Equal("[1.0.0, )", dependency.VersionRange);
    }

    [Fact]
    public void ReaderRejectsMalformedAndUnresolvedProjectReferencesAndIgnoresPackages()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "project.assets.json");
        var evidence = new RestoreEvidence(assets, "", null, null, ["target"], "", "") { Required = true };

        File.WriteAllText(assets, """
        {
          "projectFileDependencyGroups": { "target": ["Project.Dependency >= not-a-version"] },
          "targets": { "target": { "Project.Dependency/1.0.0": { "type": "project" } } }
        }
        """);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() =>
            new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence with { AssetsFileHash = Hash(assets) })).Code);

        File.WriteAllText(assets, """
        {
          "projectFileDependencyGroups": { "target": ["Malformed"] },
          "targets": { "target": { "Malformed/1.0.0": { "type": "project" } } }
        }
        """);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() =>
            new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence with { AssetsFileHash = Hash(assets) })).Code);

        File.WriteAllText(assets, """
        {
          "projectFileDependencyGroups": { "target": ["Project.Dependency "] },
          "targets": { "target": { "Project.Dependency/1.0.0": { "type": "project" } } }
        }
        """);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() =>
            new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence with { AssetsFileHash = Hash(assets) })).Code);

        File.WriteAllText(assets, """
        {
          "projectFileDependencyGroups": { "target": ["Project.Dependency >= 1.0.0"] },
          "targets": { "target": {} }
        }
        """);
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() =>
            new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence with { AssetsFileHash = Hash(assets) })).Code);

        File.WriteAllText(assets, """
        {
          "projectFileDependencyGroups": { "target": ["Package.Dependency >= not-a-version"] },
          "targets": { "target": { "Package.Dependency/1.0.0": { "type": "package" } } }
        }
        """);
        var parsed = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence with { AssetsFileHash = Hash(assets) });
        Assert.Empty(parsed.Graph);

        File.WriteAllText(assets, """
        {
          "projectFileDependencyGroups": { "target": [1], "missing": ["Missing.Dependency >= 1.0.0"] },
          "targets": { "target": {} }
        }
        """);
        var shaped = evidence with { AssetsFileHash = Hash(assets), TargetKeys = ["target"] };
        Assert.Empty(new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(shaped).Graph);

        File.WriteAllText(assets, """
        {
          "projectFileDependencyGroups": { "target": {} },
          "targets": { "target": {} }
        }
        """);
        var nonArray = evidence with { AssetsFileHash = Hash(assets) };
        Assert.Empty(new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(nonArray).Graph);
    }

    [Fact]
    public void ReaderRejectsMissingFilesAndMalformedTargetGraph()
    {
        var validator = new FileRestoreEvidenceReader(new RestoreEvidenceValidator());
        var missing = new RestoreEvidence("/tmp/no-such-assets.json", "hash", null, null, ["target"], "", "") { Required = true };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Read(missing)).Code);

        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "assets.json");
        File.WriteAllText(assets, "{\"targets\":{\"target\":[]}}");
        var evidence = new RestoreEvidence(assets, Hash(assets), null, null, ["target"], "", "") { Required = true };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Read(evidence)).Code);
        File.WriteAllText(assets, "{\"targets\":{\"target\":{\"Producer/1.0.0\":{\"type\":\"project\",\"dependencies\":{\"A\":{}}}}}}");
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => validator.Read(evidence with { AssetsFileHash = Hash(assets) })).Code);
        File.WriteAllText(assets, "{\"project\":{}}");
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => validator.Read(evidence with { AssetsFileHash = Hash(assets) })).Code);
    }

    [Fact]
    public void ReaderTreatsAssetsNodesWithoutTypeAsPackageNodes()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "assets.json");
        File.WriteAllText(assets, "{\"targets\":{\"target\":{\"Untyped/1.0.0\":{}}}}");
        var evidence = new RestoreEvidence(assets, Hash(assets), null, null, ["target"], "", "") { Required = true };
        var parsed = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence);
        Assert.Empty(parsed.Graph);
        Assert.Empty(new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence with { Required = false }).Graph);
    }

    [Fact]
    public void ReaderChecksChangedFilesGraphsAndProjectMetadataShapes()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var assets = Path.Combine(directory.Path, "assets.json");
        var lockFile = Path.Combine(directory.Path, "lock.json");
        File.WriteAllText(assets, "{\"targets\":{\"target\":{\"A/1.0.0\":{\"type\":\"package\"}}}}");
        File.WriteAllText(lockFile, "{\"dependencies\":{\"target\":{}}}");
        var evidence = new RestoreEvidence(assets, "wrong", lockFile, Hash(lockFile), ["target"], "", "") { Required = true };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(evidence)).Code);
        var changedLock = evidence with { AssetsFileHash = Hash(assets), LockFileHash = "wrong" };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(changedLock)).Code);
        File.WriteAllText(lockFile, "{\"dependencies\":{\"other\":{}}}");
        var mismatchedLockTarget = evidence with { AssetsFileHash = Hash(assets), LockFileHash = Hash(lockFile) };
        Assert.Equal(NetWasmPackErrorCode.NWPK005, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(mismatchedLockTarget)).Code);

        var suppliedGraph = changedLock with
        {
            LockFilePath = null,
            LockFileHash = null,
            Graph = [new RestoreDependencyEvidence("Different", "1.0.0", "target", false, false)]
        };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(suppliedGraph)).Code);

        File.WriteAllText(assets, "{\"project\":{\"frameworks\":{\"target\":{}}},\"targets\":{\"target\":{\"A/1.0.0\":{\"type\":\"package\"}}}}");
        var metadataShape = evidence with { AssetsFileHash = Hash(assets), LockFilePath = null, LockFileHash = null, Graph = [] };
        var parsed = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(metadataShape);
        Assert.Empty(parsed.Graph);

        File.WriteAllText(assets, "{\"targets\":{\"target\":{\"A/1.0.0\":{\"type\":4}}}}");
        var invalidPackageType = metadataShape with { AssetsFileHash = Hash(assets) };
        Assert.Equal(NetWasmPackErrorCode.NWPK004, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(invalidPackageType)).Code);

        File.WriteAllText(assets, "{\"project\":{\"frameworks\":{\"other\":{\"dependencies\":{\"A\":{\"version\":\"[1.0.0]\"}}}}},\"targets\":{\"target\":{\"Producer/1.0.0\":{\"type\":\"project\",\"dependencies\":{\"A\":\"[1.0.0]" + "\"}},\"A/1.0.0\":{\"type\":\"package\"}}}}");
        var fallback = metadataShape with { AssetsFileHash = Hash(assets) };
        var fallbackGraph = new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(fallback).Graph;
        Assert.False(fallbackGraph.Single().IsPrivate);

        File.WriteAllText(lockFile, "{\"dependencies\":{\"target\":{\"A\":{\"resolved\":\"1.0.0\"},\"B\":{\"resolved\":\"1.0.0\"}}}}");
        var extraLock = fallback with { LockFilePath = lockFile, LockFileHash = Hash(lockFile) };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(extraLock)).Code);
        File.WriteAllText(lockFile, "{\"dependencies\":{\"target\":{}}}");
        var missingLockNode = fallback with { LockFilePath = lockFile, LockFileHash = Hash(lockFile) };
        Assert.Equal(NetWasmPackErrorCode.NWPK006, Assert.Throws<NetWasmPackException>(() => new FileRestoreEvidenceReader(new RestoreEvidenceValidator()).Read(missingLockNode)).Code);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
