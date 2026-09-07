using Circle_Tracker.Storage;
using FluentAssertions;
using Moq;
using Xunit;

namespace CircleTracker.Tests.StorageTests
{
    public class CompositePlaySinkTests
    {
        [Fact]
        public void IsReady_WhenSQLiteReadyAndSheetsNotReady_ReturnsTrue()
        {
            var sqliteSink = new Mock<IPlaySink>();
            sqliteSink.Setup(s => s.IsReady).Returns(true);
            var sheetsSink = new Mock<IPlaySink>();
            sheetsSink.Setup(s => s.IsReady).Returns(false);
            var composite = new CompositePlaySink();
            composite.AddSink(sqliteSink.Object);
            composite.AddSink(sheetsSink.Object);

            var isReady = composite.IsReady;

            isReady.Should().BeTrue();
        }

        [Fact]
        public void IsReady_WhenAllSinksNotReady_ReturnsFalse()
        {
            var sink1 = new Mock<IPlaySink>();
            sink1.Setup(s => s.IsReady).Returns(false);
            var sink2 = new Mock<IPlaySink>();
            sink2.Setup(s => s.IsReady).Returns(false);
            var composite = new CompositePlaySink();
            composite.AddSink(sink1.Object);
            composite.AddSink(sink2.Object);

            var isReady = composite.IsReady;

            isReady.Should().BeFalse();
        }

        [Fact]
        public void AllSinksReady_WhenAllReady_ReturnsTrue()
        {
            var sink1 = new Mock<IPlaySink>();
            sink1.Setup(s => s.IsReady).Returns(true);
            var sink2 = new Mock<IPlaySink>();
            sink2.Setup(s => s.IsReady).Returns(true);
            var composite = new CompositePlaySink();
            composite.AddSink(sink1.Object);
            composite.AddSink(sink2.Object);

            var allReady = composite.AllSinksReady;

            allReady.Should().BeTrue();
        }

        [Fact]
        public void AllSinksReady_WhenOneSinkNotReady_ReturnsFalse()
        {
            var sink1 = new Mock<IPlaySink>();
            sink1.Setup(s => s.IsReady).Returns(true);
            var sink2 = new Mock<IPlaySink>();
            sink2.Setup(s => s.IsReady).Returns(false);
            var composite = new CompositePlaySink();
            composite.AddSink(sink1.Object);
            composite.AddSink(sink2.Object);

            var allReady = composite.AllSinksReady;

            allReady.Should().BeFalse();
        }

        [Fact]
        public void IsReady_DisabledSinksAreIgnored()
        {
            var enabledSink = new Mock<IPlaySink>();
            enabledSink.Setup(s => s.IsReady).Returns(true);
            var disabledSink = new Mock<IPlaySink>();
            disabledSink.Setup(s => s.IsReady).Returns(false);
            var composite = new CompositePlaySink();
            composite.AddSink(enabledSink.Object, () => true);
            composite.AddSink(disabledSink.Object, () => false);

            var isReady = composite.IsReady;
            var allReady = composite.AllSinksReady;

            isReady.Should().BeTrue();
            allReady.Should().BeTrue();
        }
    }
}
