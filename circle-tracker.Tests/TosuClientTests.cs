using Circle_Tracker;
using FluentAssertions;
using Moq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests
{
    public class TosuClientTests
    {
        [Fact]
        public void Should_DeserializeValidTosuV2Frame_DirectlyFromStream()
        {
            var sampleJson = @"{
                ""state"": {
                    ""number"": 2,
                    ""name"": ""Playing""
                },
                ""profile"": {
                    ""id"": 12345,
                    ""name"": ""testplayer""
                },
                ""beatmap"": {
                    ""id"": 123456,
                    ""set"": 54321,
                    ""checksum"": ""a1b2c3d4e5f6"",
                    ""title"": ""Blue Zenith"",
                    ""artist"": ""xi"",
                    ""version"": ""FOUR DIMENSIONS"",
                    ""mapper"": ""Asphyxia"",
                    ""time"": {
                        ""live"": 45000,
                        ""firstObject"": 2500,
                        ""lastObject"": 180000
                    },
                    ""stats"": {
                        ""ar"": { ""original"": 9.5, ""converted"": 9.5 },
                        ""cs"": { ""original"": 4.0, ""converted"": 4.0 },
                        ""od"": { ""original"": 9.0, ""converted"": 9.0 },
                        ""hp"": { ""original"": 5.0, ""converted"": 5.0 },
                        ""bpm"": { ""common"": 200.0, ""min"": 200.0, ""max"": 200.0 },
                        ""stars"": {
                            ""live"": 8.23,
                            ""aim"": 3.85,
                            ""speed"": 3.21,
                            ""total"": 8.23
                        }
                    }
                },
                ""play"": {
                    ""playerName"": ""testplayer"",
                    ""mode"": { ""number"": 0, ""name"": ""osu"" },
                    ""score"": 12345678,
                    ""accuracy"": 98.75,
                    ""mods"": {
                        ""number"": 72,
                        ""name"": ""HDDT""
                    },
                    ""hits"": {
                        ""300"": 450,
                        ""100"": 12,
                        ""50"": 2,
                        ""0"": 1
                    }
                },
                ""settings"": {
                    ""replayUIVisible"": false,
                    ""mode"": { ""number"": 0, ""name"": ""osu"" },
                    ""client"": { ""version"": ""b20240215.1"" }
                }
            }";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sampleJson));
            var state = JsonSerializer.Deserialize<TosuState>(stream, TosuClient.SerializerOptions);

            state.Should().NotBeNull();
            state!.State.Should().NotBeNull();
            state.State!.Number.Should().Be(2);
            state.State.Name.Should().Be("Playing");

            state.Profile.Should().NotBeNull();
            state.Profile!.Id.Should().Be(12345);
            state.Profile.Name.Should().Be("testplayer");

            state.Beatmap.Should().NotBeNull();
            state.Beatmap!.Id.Should().Be(123456);
            state.Beatmap.Set.Should().Be(54321);
            state.Beatmap.Title.Should().Be("Blue Zenith");
            state.Beatmap.Artist.Should().Be("xi");
            state.Beatmap.Version.Should().Be("FOUR DIMENSIONS");
            state.Beatmap.Checksum.Should().Be("a1b2c3d4e5f6");

            state.Beatmap.Time.Should().NotBeNull();
            state.Beatmap.Time!.Live.Should().Be(45000);
            state.Beatmap.Time.FirstObject.Should().Be(2500);

            state.Beatmap.Stats.Should().NotBeNull();
            state.Beatmap.Stats!.Ar.Should().NotBeNull();
            state.Beatmap.Stats.Ar!.Original.Should().Be(9.5m);
            state.Beatmap.Stats.Cs!.Original.Should().Be(4.0m);
            state.Beatmap.Stats.Od!.Original.Should().Be(9.0m);
            state.Beatmap.Stats.Hp!.Original.Should().Be(5.0m);

            state.Beatmap.Stats.Bpm.Should().NotBeNull();
            state.Beatmap.Stats.Bpm!.Common.Should().Be(200.0m);

            state.Beatmap.Stats.Stars.Should().NotBeNull();
            state.Beatmap.Stats.Stars!.Total.Should().Be(8.23m);
            state.Beatmap.Stats.Stars.Aim.Should().Be(3.85m);
            state.Beatmap.Stats.Stars.Speed.Should().Be(3.21m);

            state.Play.Should().NotBeNull();
            state.Play!.PlayerName.Should().Be("testplayer");
            state.Play.Accuracy.Should().Be(98.75m);
            state.Play.Score.Should().Be(12345678);

            state.Play.Mods.Should().NotBeNull();
            state.Play.Mods!.Number.Should().Be(72);
            state.Play.Mods.Name.Should().Be("HDDT");

            state.Play.Hits.Should().NotBeNull();
            state.Play.Hits!.H300.Should().Be(450);
            state.Play.Hits.H100.Should().Be(12);
            state.Play.Hits.H50.Should().Be(2);
            state.Play.Hits.Misses.Should().Be(1);

            state.Settings.Should().NotBeNull();
            state.Settings!.Client.Should().NotBeNull();
            state.Settings.Client!.Version.Should().Be("b20240215.1");
        }

        [Fact]
        public void Should_HandleMissingOrMalformedFields_WithoutThrowing()
        {
            var incompleteJson = @"{
                ""state"": {
                    ""number"": 2
                },
                ""beatmap"": {
                    ""id"": 999,
                    ""stats"": {
                        ""stars"": {
                            ""total"": ""7.5""
                        }
                    }
                },
                ""play"": {
                    ""accuracy"": ""98.5"",
                    ""hits"": {
                        ""300"": 100
                    }
                }
            }";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(incompleteJson));

            TosuState? state = null;
            var act = () => { state = JsonSerializer.Deserialize<TosuState>(stream, TosuClient.SerializerOptions); };

            act.Should().NotThrow();

            state.Should().NotBeNull();
            state!.State.Should().NotBeNull();
            state.State!.Number.Should().Be(2);

            state.Beatmap.Should().NotBeNull();
            state.Beatmap!.Id.Should().Be(999);
            state.Beatmap.Stats.Should().NotBeNull();
            state.Beatmap.Stats!.Stars.Should().NotBeNull();
            state.Beatmap.Stats.Stars!.Total.Should().Be(7.5m);

            state.Play.Should().NotBeNull();
            state.Play!.Accuracy.Should().Be(98.5m);
            state.Play.Hits.Should().NotBeNull();
            state.Play.Hits!.H300.Should().Be(100);
        }

        [Fact]
        public void Should_HandleExtraUnknownFields_Gracefully()
        {
            var jsonWithExtraFields = @"{
                ""state"": {
                    ""number"": 5,
                    ""name"": ""SongSelect"",
                    ""unknownField"": ""should be ignored""
                },
                ""beatmap"": {
                    ""id"": 777,
                    ""newFeature"": 123,
                    ""stats"": {
                        ""stars"": {
                            ""total"": 6.0,
                            ""experimental"": 99
                        }
                    }
                },
                ""futureFeature"": {
                    ""data"": ""ignored""
                }
            }";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonWithExtraFields));

            TosuState? state = null;
            var act = () => { state = JsonSerializer.Deserialize<TosuState>(stream, TosuClient.SerializerOptions); };

            act.Should().NotThrow();

            state.Should().NotBeNull();
            state!.State.Should().NotBeNull();
            state.State!.Number.Should().Be(5);
            state.State.Name.Should().Be("SongSelect");

            state.Beatmap.Should().NotBeNull();
            state.Beatmap!.Id.Should().Be(777);
            state.Beatmap.Stats.Should().NotBeNull();
            state.Beatmap.Stats!.Stars.Should().NotBeNull();
            state.Beatmap.Stats.Stars!.Total.Should().Be(6.0m);
        }

        [Fact]
        public void Should_ParseNumericStrings_AsNumbers()
        {
            var jsonWithStringNumbers = @"{
                ""beatmap"": {
                    ""id"": ""12345"",
                    ""stats"": {
                        ""ar"": { ""original"": ""9.0"", ""converted"": 9.0 },
                        ""stars"": {
                            ""total"": ""8.23"",
                            ""aim"": 3.5,
                            ""speed"": ""2.8""
                        }
                    }
                },
                ""play"": {
                    ""accuracy"": ""99.5""
                }
            }";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonWithStringNumbers));

            TosuState? state = null;
            var act = () => { state = JsonSerializer.Deserialize<TosuState>(stream, TosuClient.SerializerOptions); };

            act.Should().NotThrow();

            state.Should().NotBeNull();
            state!.Beatmap.Should().NotBeNull();
            state.Beatmap!.Id.Should().Be(12345);
            state.Beatmap.Stats.Should().NotBeNull();
            state.Beatmap.Stats!.Ar.Should().NotBeNull();
            state.Beatmap.Stats.Ar!.Original.Should().Be(9.0m);
            state.Beatmap.Stats.Stars.Should().NotBeNull();
            state.Beatmap.Stats.Stars!.Total.Should().Be(8.23m);
            state.Beatmap.Stats.Stars.Speed.Should().Be(2.8m);

            state.Play.Should().NotBeNull();
            state.Play!.Accuracy.Should().Be(99.5m);
        }

        [Fact]
        public void Should_HandleNullValues_Gracefully()
        {
            var jsonWithNulls = @"{
                ""state"": {
                    ""number"": 0,
                    ""name"": null
                },
                ""profile"": null,
                ""beatmap"": {
                    ""id"": 100,
                    ""checksum"": null,
                    ""stats"": null
                }
            }";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonWithNulls));

            TosuState? state = null;
            var act = () => { state = JsonSerializer.Deserialize<TosuState>(stream, TosuClient.SerializerOptions); };

            act.Should().NotThrow();

            state.Should().NotBeNull();
            state!.State.Should().NotBeNull();
            state.State!.Number.Should().Be(0);
            state.State.Name.Should().BeNull();

            state.Profile.Should().BeNull();

            state.Beatmap.Should().NotBeNull();
            state.Beatmap!.Id.Should().Be(100);
            state.Beatmap.Checksum.Should().BeNull();
            state.Beatmap.Stats.Should().BeNull();
        }

        [Fact]
        public async Task Should_DeserializeAsync_FromStream()
        {
            var sampleJson = @"{
                ""state"": { ""number"": 2, ""name"": ""Playing"" },
                ""beatmap"": {
                    ""id"": 555,
                    ""stats"": {
                        ""stars"": { ""total"": 7.0 }
                    }
                }
            }";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sampleJson));
            var state = await JsonSerializer.DeserializeAsync<TosuState>(stream, TosuClient.SerializerOptions);

            state.Should().NotBeNull();
            state!.State.Should().NotBeNull();
            state.State!.Number.Should().Be(2);
            state.Beatmap.Should().NotBeNull();
            state.Beatmap!.Id.Should().Be(555);
            state.Beatmap.Stats.Should().NotBeNull();
            state.Beatmap.Stats!.Stars.Should().NotBeNull();
            state.Beatmap.Stats.Stars!.Total.Should().Be(7.0m);
        }

        [Fact]
        public async Task ConnectAsync_AlreadyConnected_IsNoop()
        {
            using var client = new TosuClient { Host = "127.0.0.1", Port = 99999 };

            await client.ConnectAsync();
            await Task.Delay(50);

            await client.ConnectAsync();

            await client.DisconnectAsync();
        }

        [Fact]
        public async Task DisconnectAsync_NotConnected_DoesNotThrow()
        {
            using var client = new TosuClient { Host = "127.0.0.1", Port = 99999 };

            var act = async () => await client.DisconnectAsync();

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task DisconnectAsync_WhileConnecting_CancelsCleanly()
        {
            using var client = new TosuClient { Host = "127.0.0.1", Port = 99999 };

            var connectTask = client.ConnectAsync();
            await Task.Delay(10);
            await client.DisconnectAsync();

            client.IsConnected.Should().BeFalse();
        }

        [Fact]
        public void LatestState_SetFromWebSocket_AccessibleFromProperty()
        {
            using var client = new TosuClient();

            client.LatestState.Should().BeNull();
        }

        [Fact]
        public void IsConnected_InitialState_IsFalse()
        {
            using var client = new TosuClient();

            client.IsConnected.Should().BeFalse();
        }

        [Fact]
        public async Task ConnectionStateChanged_OnConnect_FiresEvent()
        {
            using var server = new CircleTracker.Tests.Mocks.MockTosuWebSocketServer();
            using var client = new TosuClient { Host = "127.0.0.1", Port = server.Port };

            bool connectedFired = false;
            client.ConnectionStateChanged += (sender, isConnected) =>
            {
                if (isConnected) connectedFired = true;
            };

            await client.ConnectAsync();
            await server.WaitForClientConnectionAsync(TimeSpan.FromSeconds(2));

            await Task.Delay(100);

            connectedFired.Should().BeTrue();

            await client.DisconnectAsync();
        }

        [Fact]
        public async Task ConnectionStateChanged_OnDisconnect_FiresEvent()
        {
            using var server = new CircleTracker.Tests.Mocks.MockTosuWebSocketServer();
            using var client = new TosuClient { Host = "127.0.0.1", Port = server.Port };

            bool disconnectedFired = false;
            client.ConnectionStateChanged += (sender, isConnected) =>
            {
                if (!isConnected) disconnectedFired = true;
            };

            await client.ConnectAsync();
            await server.WaitForClientConnectionAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(50);

            await client.DisconnectAsync();
            await Task.Delay(100);

            disconnectedFired.Should().BeTrue();
        }

        [Fact]
        public async Task Deserialize_MalformedJson_DoesNotCrash()
        {
            using var server = new CircleTracker.Tests.Mocks.MockTosuWebSocketServer();
            using var client = new TosuClient { Host = "127.0.0.1", Port = server.Port };

            await client.ConnectAsync();
            await server.WaitForClientConnectionAsync(TimeSpan.FromSeconds(2));

            await server.BroadcastJsonAsync("{invalid json", default);
            await Task.Delay(100);

            await client.DisconnectAsync();
        }

        [Fact]
        public async Task Deserialize_PartialTosuState_HandlesGracefully()
        {
            using var server = new CircleTracker.Tests.Mocks.MockTosuWebSocketServer();
            using var client = new TosuClient { Host = "127.0.0.1", Port = server.Port };

            TosuState? receivedState = null;
            client.StateUpdated += (sender, state) => receivedState = state;

            await client.ConnectAsync();
            await server.WaitForClientConnectionAsync(TimeSpan.FromSeconds(2));

            var partialJson = @"{""state"":{""number"":2}}";
            await server.BroadcastJsonAsync(partialJson, default);
            await Task.Delay(100);

            receivedState.Should().NotBeNull();
            receivedState!.State.Should().NotBeNull();
            receivedState.State!.Number.Should().Be(2);

            await client.DisconnectAsync();
        }

        [Fact]
        public async Task Deserialize_EmptyJson_DoesNotCrash()
        {
            using var server = new CircleTracker.Tests.Mocks.MockTosuWebSocketServer();
            using var client = new TosuClient { Host = "127.0.0.1", Port = server.Port };

            await client.ConnectAsync();
            await server.WaitForClientConnectionAsync(TimeSpan.FromSeconds(2));

            await server.BroadcastJsonAsync("{}", default);
            await Task.Delay(100);

            await client.DisconnectAsync();
        }

        [Fact]
        public async Task CalculatePpAsync_ServerDown_ReturnsNull()
        {
            using var client = new TosuClient { Host = "127.0.0.1", Port = 99999 };

            var result = await client.CalculatePpAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task CalculatePpAsync_CancellationRequested_ThrowsOperationCancelled()
        {
            using var client = new TosuClient { Host = "127.0.0.1", Port = 24050 };
            using var cts = new System.Threading.CancellationTokenSource();
            cts.CancelAfter(1);

            var act = async () => await client.CalculatePpAsync(0, cts.Token);

            await act.Should().ThrowAsync<System.OperationCanceledException>();
        }

        [Fact]
        public void Dispose_CleansUpResources()
        {
            var client = new TosuClient();

            var act = () => client.Dispose();

            act.Should().NotThrow();
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            var client = new TosuClient();

            client.Dispose();
            var act = () => client.Dispose();

            act.Should().NotThrow();
        }

        [Fact]
        public async Task ConnectAsync_CalledConcurrently_StartsOnlyOneRunner()
        {
            var mockTransport = new Mock<ITosuTransport>();
            var runnerCount = 0;
            mockTransport
                .Setup(t => t.RunWebSocketSessionAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .Returns(async (byte[] _, CancellationToken ct) =>
                {
                    Interlocked.Increment(ref runnerCount);
                    await Task.Delay(100, ct);
                    return false;
                });
            mockTransport
                .Setup(t => t.PollHttpSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken ct) =>
                {
                    await Task.Delay(100, ct);
                    return null;
                });
            using var client = new TosuClient(mockTransport.Object);

            await Task.WhenAll(client.ConnectAsync(), client.ConnectAsync());
            await Task.Delay(50);
            await client.DisconnectAsync();

            runnerCount.Should().Be(1);
        }

        [Fact]
        public async Task ConnectAsync_AfterDisconnect_CanReconnect()
        {
            var mockTransport = new Mock<ITosuTransport>();
            mockTransport
                .Setup(t => t.RunWebSocketSessionAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            mockTransport
                .Setup(t => t.PollHttpSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TosuState { State = new TosuGameState { Number = 2 } });
            using var client = new TosuClient(mockTransport.Object);

            await client.ConnectAsync();
            await Task.Delay(50);
            await client.DisconnectAsync();
            await client.ConnectAsync();
            await Task.Delay(50);
            var isConnected = client.IsConnected;
            await client.DisconnectAsync();

            isConnected.Should().BeTrue();
        }

        [Fact]
        public async Task ConnectAsync_WhileAlreadyConnected_ReturnsImmediately()
        {
            var mockTransport = new Mock<ITosuTransport>();
            mockTransport
                .Setup(t => t.RunWebSocketSessionAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .Returns(async (byte[] _, CancellationToken ct) =>
                {
                    await Task.Delay(1000, ct);
                    return false;
                });
            using var client = new TosuClient(mockTransport.Object);
            await client.ConnectAsync();
            var initialRunner = client.RunnerTask;

            await client.ConnectAsync();
            var secondRunner = client.RunnerTask;
            await client.DisconnectAsync();

            secondRunner.Should().BeSameAs(initialRunner);
        }
    }
}
