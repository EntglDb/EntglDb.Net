using System;
using System.IO;
using System.Threading.Tasks;
using EntglDb.Network.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EntglDb.Network.Tests
{
    public class TelemetryTests : IDisposable
    {
        private readonly string _tempFile;

        public TelemetryTests()
        {
            _tempFile = Path.GetTempFileName();
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        [Fact]
        public async Task Should_Record_And_Persist_Metrics()
        {
            // Arrange
            using var service = new NetworkTelemetryService(NullLogger<NetworkTelemetryService>.Instance, _tempFile);

            // Act
            // Record some values for CompressionRatio
            service.RecordValue(MetricType.CompressionRatio, 0.5);
            service.RecordValue(MetricType.CompressionRatio, 0.7);
            
            // Record time metric
            using (var timer = service.StartMetric(MetricType.EncryptionTime))
            {
                await Task.Delay(10); // Should be > 0 ms
            }

            // Allow channel to process
            await Task.Delay(500);

            // Force persist to file
            service.ForcePersist();

            // Assert
            File.Exists(_tempFile).Should().BeTrue();
            var fileInfo = new FileInfo(_tempFile);
            fileInfo.Length.Should().BeGreaterThan(0);

            using var fs = File.OpenRead(_tempFile);
            using var br = new BinaryReader(fs);

            // Header
            byte version = br.ReadByte();
            version.Should().Be(1);
            long timestamp = br.ReadInt64();
            timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 5);

            // Metrics
            // We expect all MetricTypes
            int typeCount = Enum.GetValues(typeof(MetricType)).Length;
            
            bool foundCompression = false;
            bool foundEncryption = false;

            for (int i = 0; i < typeCount; i++)
            {
                int typeInt = br.ReadInt32();
                var type = (MetricType)typeInt;
                
                // 4 Windows per type
                for (int w = 0; w < 4; w++)
                {
                    int window = br.ReadInt32(); // 60, 300, 600, 1800
                    double avg = br.ReadDouble();

                    if (type == MetricType.CompressionRatio && window == 60)
                    {
                        // Avg of 0.5 and 0.7 is 0.6
                        avg.Should().BeApproximately(0.6, 0.001);
                        foundCompression = true;
                    }

                    if (type == MetricType.EncryptionTime && window == 60)
                    {
                        avg.Should().BeGreaterThan(0);
                        foundEncryption = true;
                    }
                }
            }

            foundCompression.Should().BeTrue();
            foundEncryption.Should().BeTrue();
        }
    }
}
