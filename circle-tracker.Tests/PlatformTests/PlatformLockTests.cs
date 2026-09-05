using Circle_Tracker;
using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace CircleTracker.Tests.PlatformTests
{
    public class PlatformLockTests
    {
        [Fact]
        public void Should_GenerateUserSpecificLockFileName_When_FallbackTempPathUsed()
        {
            string simulatedUser = "circleuser";
            string simulatedTemp = "/tmp/test-ct";

            string fallbackLockPath = SingleInstanceLock.GetLockFilePath(
                xdgRuntimeDir: null,
                tempPath: simulatedTemp,
                userName: simulatedUser);

            fallbackLockPath.Should().Be(Path.Combine(simulatedTemp, $"circle-tracker-{simulatedUser}.lock"));
            fallbackLockPath.Should().Contain(simulatedUser);

            string xdgDir = "/run/user/1000";
            string xdgLockPath = SingleInstanceLock.GetLockFilePath(
                xdgRuntimeDir: xdgDir,
                tempPath: simulatedTemp,
                userName: simulatedUser);

            xdgLockPath.Should().Be(Path.Combine(xdgDir, "circle-tracker.lock"));
        }

        [Fact]
        public void Should_AcquireAndReleaseLockCleanly()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"ct_lock_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string lockPath = Path.Combine(tempDir, "test.lock");

            try
            {
                FileStream? lockHandle = SingleInstanceLock.TryAcquire(lockPath);

                lockHandle.Should().NotBeNull();
                File.Exists(lockPath).Should().BeTrue();

                Action secondAcquisition = () =>
                {
                    using var secondHandle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                };
                secondAcquisition.Should().Throw<IOException>();

                SingleInstanceLock.Release(lockHandle, lockPath);

                File.Exists(lockPath).Should().BeFalse();

                using FileStream? reacquiredHandle = SingleInstanceLock.TryAcquire(lockPath);
                reacquiredHandle.Should().NotBeNull();
                SingleInstanceLock.Release(reacquiredHandle, lockPath);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        [Fact]
        public void Should_NotThrow_When_ReleasingNullOrNonExistentLock()
        {
            string nonExistentPath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.lock");

            Action act = () => SingleInstanceLock.Release(null, nonExistentPath);

            act.Should().NotThrow();
        }

        [Fact]
        public void Should_ResolveLockFilePathFromEnvironment_When_DefaultParameterlessMethodCalled()
        {
            string path = SingleInstanceLock.GetLockFilePath();

            path.Should().NotBeNullOrWhiteSpace();
            path.Should().EndWith(".lock");
        }
    }
}
