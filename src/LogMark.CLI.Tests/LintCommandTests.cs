using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class LintCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;

    public LintCommandTests()
    {
        _vault = new TestVaultBuilder().WithStandardStructure();
    }

    public void Dispose() => _vault.Dispose();

    // Writes two diary files with #ml / #ML so TagCaseVariantLint fires.
    // Mirrors the existing scan-test seed pattern exactly.
    private void SeedTagCaseVariant()
    {
        var diaryPath = Path.Combine(_vault.LogMarkPath, "_diary");
        File.WriteAllText(Path.Combine(diaryPath, "2026a.md"), "## 2026-05-30 10:00\n\nnote about #ml models\n");
        File.WriteAllText(Path.Combine(diaryPath, "2026b.md"), "## 2026-05-30 10:01\n\nanother about #ML models\n");
    }

    [Fact]
    public void Lint_NoSubcommand_ShowsUsage()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "lint");
        result.ExitCode.Should().Be(1);
        result.StdOut.Should().Contain("scan");
        result.StdOut.Should().Contain("fix");
    }

    [Fact]
    public void Lint_UnknownSubcommand_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "lint", "bogus");
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Unknown");
    }

    [Fact]
    public void LintScan_CleanVault_ReportsNoIssues()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "lint", "scan");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("No lint issues");
    }

    [Fact]
    public void LintScan_TagCaseVariant_ReportsIssue()
    {
        // Seed two entry files directly: one with #ml, one with #ML so TagCaseVariantLint fires.
        // Using direct file writes (same pattern as LintBootstrapTests) rather than lm capture
        // because capture normalizes routes and the entry cache warmup timing cannot be controlled.
        SeedTagCaseVariant();

        var result = CliRunner.Run("--vault", _vault.VaultPath, "lint", "scan");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("tags");
    }

    [Fact]
    public void LintScan_Json_EmitsParseableJson()
    {
        SeedTagCaseVariant();

        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "lint", "scan");
        result.ExitCode.Should().Be(0);
        var trimmed = result.StdOut.TrimStart();
        trimmed.Should().StartWith("[");
        result.StdOut.Should().Contain("\"ruleId\"");
    }

    [Fact]
    public void LintScan_ExitCode_CleanVault_Exits0()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "lint", "scan", "--exit-code");
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public void LintScan_ExitCode_DirtyVault_Exits1()
    {
        SeedTagCaseVariant();

        var result = CliRunner.Run("--vault", _vault.VaultPath, "lint", "scan", "--exit-code");
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public void LintFix_DryRunByDefault_DoesNotModifyAndExits0()
    {
        SeedTagCaseVariant();

        var dry = CliRunner.Run("--vault", _vault.VaultPath, "lint", "fix");

        dry.ExitCode.Should().Be(0);
        dry.StdOut.Should().Contain("DRY RUN");
        // Re-scanning still finds the issue (nothing was applied).
        var scan = CliRunner.Run("--vault", _vault.VaultPath, "lint", "scan");
        scan.StdOut.Should().Contain("tags");
    }

    [Fact]
    public void LintFix_Apply_FixesTagCaseVariant()
    {
        SeedTagCaseVariant();

        var apply = CliRunner.Run("--vault", _vault.VaultPath, "lint", "fix", "--apply");

        apply.ExitCode.Should().Be(0);
        apply.StdOut.Should().Contain("Fixed");
        // After applying, a fresh scan reports no tag-case-variant.
        var scan = CliRunner.Run("--vault", _vault.VaultPath, "lint", "scan");
        scan.StdOut.Should().NotContain("tag-case-variant");
    }

    // Seed an orphaned folder: a non-underscore, non-system folder under logmark/ that
    // contains an entry pool (_ideas/2026.md) but is absent from routes.md. Mirrors
    // OrphanedEntryLintTests.PlantEntry exactly. Also writes routes.md in the format
    // the RouteService parser expects (## section + ### name + - path:).
    private void SeedOrphanFolder()
    {
        var systemPath = Path.Combine(_vault.LogMarkPath, "_system");
        Directory.CreateDirectory(systemPath);
        // Minimal valid routes.md with no entry for 'orphan-test'
        File.WriteAllText(
            Path.Combine(systemPath, "routes.md"),
            "## Projects\n\n### acme\n- path: projects/acme\n");

        // Create the orphaned folder with an entry pool so HasEntries returns true.
        var ideasDir = Path.Combine(_vault.LogMarkPath, "orphan-test", "_ideas");
        Directory.CreateDirectory(ideasDir);
        File.WriteAllText(
            Path.Combine(ideasDir, "2026.md"),
            "## 2026-05-30 10:00\n\nseed entry for orphan\n");
    }

    [Fact]
    public void LintFix_RoutesExcludedByDefault_OrphanRemainsAfterDefaultFix()
    {
        // Seed a genuine orphan-folder route issue.
        SeedOrphanFolder();

        // Sanity: scan reports the orphan-folder issue in the routes category.
        var scan = CliRunner.Run("--vault", _vault.VaultPath, "lint", "scan");
        scan.ExitCode.Should().Be(0);
        scan.StdOut.Should().Contain("orphan-folder",
            "expected scan to surface the orphaned folder as a routes issue");

        // Default fix scope is tags+entries only; the orphan (routes category) is not in scope.
        var fix = CliRunner.Run("--vault", _vault.VaultPath, "lint", "fix", "--apply");
        fix.ExitCode.Should().Be(0);
        fix.StdOut.Should().NotContain("[routes]");

        // After the default fix, re-scan still reports the orphan -- it was not touched.
        var reScan = CliRunner.Run("--vault", _vault.VaultPath, "lint", "scan");
        reScan.StdOut.Should().Contain("orphan-folder",
            "orphan-folder issue must persist after default-scope fix");
    }

    [Fact]
    public void LintFix_RoutesCategory_OrphanIsNonBatchable_NotAutoFixed()
    {
        // Seed a genuine orphan-folder route issue.
        SeedOrphanFolder();

        // Even with --category routes --apply, the orphan action is non-batchable,
        // so the batch path gets zero eligible issues and creates nothing.
        var fix = CliRunner.Run("--vault", _vault.VaultPath, "lint", "fix",
            "--category", "routes", "--apply");
        fix.ExitCode.Should().Be(0);
        // "Fixed 0" -- the batch run applied nothing for the orphan.
        fix.StdOut.Should().Contain("Fixed 0",
            "orphan-folder action is non-batchable; headless fix must not create routes");
        // The issue is reported as needing manual review.
        fix.StdOut.Should().Contain("manual review",
            "non-batchable issues are reported as needing manual review");

        // Re-scan still finds the orphan -- no route was created.
        var reScan = CliRunner.Run("--vault", _vault.VaultPath, "lint", "scan");
        reScan.StdOut.Should().Contain("orphan-folder",
            "orphan-folder issue must still be present after --category routes --apply");
    }

    [Fact]
    public void LintFix_Json_DryRun_EmitsSummary()
    {
        SeedTagCaseVariant();

        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "lint", "fix");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"dryRun\": true");
        result.StdOut.Should().Contain("\"wouldFix\"");
    }
}
