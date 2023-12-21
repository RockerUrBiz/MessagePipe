using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;
using Xunit.Abstractions;

namespace MessagePipe.Interprocess.Tests
{
    public class TcpTest_Ex
    {
        readonly ITestOutputHelper helper;

        public TcpTest_Ex(ITestOutputHelper testOutputHelper)
        {
            this.helper = testOutputHelper;
        }

        [Fact]
        public async Task SimpleIntInt_Ex()
        {
            var provider = TestHelper.BuildServiceProviderTcpEx("127.0.0.1", 1784, helper);
            using (provider as IDisposable)
            {
                var p1 = provider.GetRequiredService<IDistributedPublisher<int, int>>();
                var s1 = provider.GetRequiredService<IDistributedSubscriber<int, int>>();

                var result = new List<int>();
                await s1.SubscribeAsync(1, x =>
                {
                    result.Add(x);
                });

                var result2 = new List<int>();
                await s1.SubscribeAsync(4, x =>
                {
                    result2.Add(x);
                });

                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync(1, 9999);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync(4, 888);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync(1, 4999);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...

                result.Should().Equal(9999, 4999);
                result2.Should().Equal(888);
            }
        }

        [Fact]
        public async Task TwoPublisher_Ex()
        {
            var provider = TestHelper.BuildServiceProviderTcpEx("127.0.0.1", 1992, helper, asServer: false);
            var provider2 = TestHelper.BuildServiceProviderTcpEx("127.0.0.1", 1992, helper, asServer: false);
            using (provider as IDisposable)
            using (provider2 as IDisposable)
            {
                var p1 = provider.GetRequiredService<IDistributedPublisher<int, int>>();
                var s1 = provider.GetRequiredService<IDistributedSubscriber<int, int>>();
                var p2 = provider2.GetRequiredService<IDistributedPublisher<int, int>>();

                var result = new List<int>();
                await s1.SubscribeAsync(1, x =>
                {
                    result.Add(x);
                });

                var result2 = new List<int>();
                await s1.SubscribeAsync(4, x =>
                {
                    result2.Add(x);
                });

                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync(1, 9999);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p2.PublishAsync(4, 888);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p2.PublishAsync(1, 4999);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...

                result.Should().Equal(9999, 4999);
                result2.Should().Equal(888);
            }
        }

        [Fact]
        public async Task SimpleStringString_Ex()
        {
            var provider = TestHelper.BuildServiceProviderTcpEx("127.0.0.1", 1436, helper);
            using (provider as IDisposable)
            {
                var p1 = provider.GetRequiredService<IDistributedPublisher<string, string>>();
                var s1 = provider.GetRequiredService<IDistributedSubscriber<string, string>>();

                var result = new List<string>();
                await s1.SubscribeAsync("hogemogeman", x =>
                {
                    result.Add(x);
                });

                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync("hogemogeman", "abidatoxurusika");

                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...

                result.Should().Equal("abidatoxurusika");
            }
        }

        [Fact]
        public async Task HugeSizeTest_Ex()
        {
            var provider = TestHelper.BuildServiceProviderTcpEx("127.0.0.1", 1436, helper);
            using (provider as IDisposable)
            {
                var p1 = provider.GetRequiredService<IDistributedPublisher<string, string>>();
                var s1 = provider.GetRequiredService<IDistributedSubscriber<string, string>>();

                var result = new List<string>();
                await s1.SubscribeAsync("hogemogeman", x =>
                {
                    result.Add(x);
                });

                var ldata = new string('a', 99999);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync("hogemogeman", ldata);

                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...

                result.Should().Equal(ldata);
            }
        }

        [Fact]
        public async Task MoreHugeSizeTest_Ex()
        {
            var provider = TestHelper.BuildServiceProviderTcpEx("127.0.0.1", 1436, helper);
            using (provider as IDisposable)
            {
                var p1 = provider.GetRequiredService<IDistributedPublisher<string, string>>();
                var s1 = provider.GetRequiredService<IDistributedSubscriber<string, string>>();

                var result = new List<string>();
                await s1.SubscribeAsync("hogemogeman", x =>
                {
                    result.Add(x);
                });

                var ldata1 = new string('a', 99999);
                var ldata2 = new string('b', 99999);
                var ldata3 = new string('c', 99999);
                var ldata = string.Concat(ldata1, ldata2, ldata3);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync("hogemogeman", ldata);

                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...

                result.Should().Equal(ldata);
            }
        }


        [Fact]
        public async Task RemoteRequestTest_Ex()
        {
            var provider = TestHelper.BuildServiceProviderTcpEx("127.0.0.1", 1355, helper, asServer: true);
            using (provider as IDisposable)
            {
                var remoteHandler = provider.GetRequiredService<IRemoteRequestHandler<int, string>>();

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

                var v = await remoteHandler.InvokeAsync(9999, cts.Token);
                v.Should().Be("ECHO:9999");

                var v2 = await remoteHandler.InvokeAsync(4444);
                v2.Should().Be("ECHO:4444");

                var ex = await Assert.ThrowsAsync<RemoteRequestException>(async () =>
                {
                    var v3 = await remoteHandler.InvokeAsync(-1);
                });
                ex.Message.Should().Contain("NO -1");
            }
        }

        [Fact]
        public async Task MultipleSubscriber_Ex()
        {
            var provider = TestHelper.BuildServiceProviderTcpEx("127.0.0.1", 1992, helper, asServer: false);
            var provider1 = TestHelper.BuildServiceProviderTcpEx("127.0.0.1", 1992, helper, asServer: false);

            using (provider as IDisposable)
            using (provider1 as IDisposable)
            {
                var p1 = provider.GetRequiredService<IDistributedPublisher<string, int>>();

                var s1 = provider1.GetRequiredService<IDistributedSubscriber<string, int>>();
                var s1ResultOne = new List<int>();
                await s1.SubscribeAsync("one", x => { s1ResultOne.Add(x); });
                var s1ResultTwo = new List<int>();
                await s1.SubscribeAsync("two", x => { s1ResultTwo.Add(x); });

                var s2 = provider1.GetRequiredService<IDistributedSubscriber<string, int>>();
                var s2ResultOne = new List<int>();
                await s2.SubscribeAsync("one", x => { s2ResultOne.Add(x); });
                var s2ResultTwo = new List<int>();
                await s2.SubscribeAsync("two", x => { s2ResultTwo.Add(x); });

                var s3 = provider1.GetRequiredService<IDistributedSubscriber<string, int>>();
                var s3ResultOne = new List<int>();
                await s3.SubscribeAsync("one", x => { s3ResultOne.Add(x); });
                var s3ResultTwo = new List<int>();
                await s3.SubscribeAsync("two", x => { s3ResultTwo.Add(x); });

                var s4 = provider1.GetRequiredService<IDistributedSubscriber<string, int>>();
                var s4ResultOne = new List<int>();
                await s4.SubscribeAsync("one", x => { s4ResultOne.Add(x); });
                var s4ResultTwo = new List<int>();
                await s4.SubscribeAsync("two", x => { s4ResultTwo.Add(x); });

                var s5 = provider1.GetRequiredService<IDistributedSubscriber<string, int>>();
                var s5ResultOne = new List<int>();
                await s5.SubscribeAsync("one", x => { s5ResultOne.Add(x); });
                var s5ResultTwo = new List<int>();
                await s5.SubscribeAsync("two", x => { s5ResultTwo.Add(x); });

                var s6 = provider1.GetRequiredService<IDistributedSubscriber<string, int>>();
                var s6ResultOne = new List<int>();
                await s6.SubscribeAsync("one", x => { s6ResultOne.Add(x); });
                var s6ResultTwo = new List<int>();
                await s6.SubscribeAsync("two", x => { s6ResultTwo.Add(x); });

                var s7 = provider1.GetRequiredService<IDistributedSubscriber<string, int>>();
                var s7ResultOne = new List<int>();
                await s7.SubscribeAsync("one", x => { s7ResultOne.Add(x); });
                var s7ResultTwo = new List<int>();
                await s7.SubscribeAsync("two", x => { s7ResultTwo.Add(x); });

                var s8 = provider1.GetRequiredService<IDistributedSubscriber<string, int>>();
                var s8ResultOne = new List<int>();
                await s8.SubscribeAsync("one", x => { s8ResultOne.Add(x); });
                var s8ResultTwo = new List<int>();
                await s8.SubscribeAsync("two", x => { s8ResultTwo.Add(x); });

                var s9 = provider1.GetRequiredService<IDistributedSubscriber<string, int>>();
                var s9ResultOne = new List<int>();
                await s9.SubscribeAsync("one", x => { s9ResultOne.Add(x); });
                var s9ResultTwo = new List<int>();
                await s9.SubscribeAsync("two", x => { s9ResultTwo.Add(x); });


                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync("one", 9999);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync("two", 888);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...
                await p1.PublishAsync("one", 4999);
                await Task.Delay(TimeSpan.FromSeconds(1)); // wait for receive data...

                s1ResultOne.Should().Equal(9999, 4999);
                s1ResultTwo.Should().Equal(888);
                s2ResultOne.Should().Equal(9999, 4999);
                s2ResultTwo.Should().Equal(888);
                s3ResultOne.Should().Equal(9999, 4999);
                s3ResultTwo.Should().Equal(888);
                s4ResultOne.Should().Equal(9999, 4999);
                s4ResultTwo.Should().Equal(888);
                s5ResultOne.Should().Equal(9999, 4999);
                s5ResultTwo.Should().Equal(888);
                s6ResultOne.Should().Equal(9999, 4999);
                s6ResultTwo.Should().Equal(888);
                s7ResultOne.Should().Equal(9999, 4999);
                s7ResultTwo.Should().Equal(888);
                s8ResultOne.Should().Equal(9999, 4999);
                s8ResultTwo.Should().Equal(888);
                s9ResultOne.Should().Equal(9999, 4999);
                s9ResultTwo.Should().Equal(888);
            }
        }
    }
}
