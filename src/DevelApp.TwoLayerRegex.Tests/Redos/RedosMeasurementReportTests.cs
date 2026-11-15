using System.Text.Json;
using DevelApp.TwoLayerRegex.Tests.Redos;
using Xunit;

namespace DevelApp.TwoLayerRegex.Tests
{
    /// <summary>
    /// Generates the committed measurement report and validates its
    /// contents. CI runs smoke mode (1/10 KB grid, short ladder); the
    /// committed docs/measurements report is regenerated with
    /// REDOS_REPORT=full on a quiet machine:
    ///
    ///   REDOS_REPORT=full dotnet test src/DevelApp.TwoLayerRegex.Tests \
    ///     --filter "FullyQualifiedName~RedosMeasurementReportTests"
    ///
    /// The test writes redos-immunity-report.json next to the test assembly;
    /// copy it to docs/measurements/redos-immunity/ and update the README.
    /// </summary>
    public sealed class RedosMeasurementReportTests
    {
        [Fact]
        public void Report_is_generated_with_correct_verdicts_and_bounded_times()
        {
            string json = RedosMeasurementReport.Build();

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal(
                RedosMeasurementReport.FullMode ? "full" : "smoke",
                root.GetProperty("mode").GetString());

            // Every grid row must carry the correct verdict within the cap.
            var grid = root.GetProperty("grid");
            Assert.True(grid.GetArrayLength() >= RedosPatternCatalog.ArticleAndCorpusCases.Count,
                "The grid must cover every article/corpus pattern.");
            foreach (var row in grid.EnumerateArray())
            {
                Assert.Equal("correct", row.GetProperty("verdict").GetString());
                Assert.False(row.GetProperty("budgetExceeded").GetBoolean());
                Assert.True(row.GetProperty("ms").GetDouble() < 10_000,
                    $"Grid row {row.GetProperty("caseName").GetString()} at {row.GetProperty("length").GetInt32()} chars exceeded the 10 s cap.");
            }

            // Scaling rows must be linear within the envelope.
            var scaling = root.GetProperty("scaling");
            Assert.Equal(RedosPatternCatalog.ScalingCases.Count, scaling.GetArrayLength());
            foreach (var row in scaling.EnumerateArray())
            {
                Assert.True(row.GetProperty("r2").GetDouble() >= 0.90,
                    $"Scaling case {row.GetProperty("caseName").GetString()} has R² < 0.90.");
            }

            // The known characteristic must be recorded honestly.
            Assert.Equal("a+b", root.GetProperty("unanchoredCharacteristic").GetProperty("pattern").GetString());

            // Persist next to the test assembly for the committed copy.
            string path = Path.Combine(AppContext.BaseDirectory, "redos-immunity-report.json");
            File.WriteAllText(path, json);
        }
    }
}
