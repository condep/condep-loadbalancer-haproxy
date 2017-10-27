using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ConDep.Dsl.Config;
using ConDep.Dsl.Logging;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using Mock4Net.Core;
using NUnit.Framework;
using Logger = ConDep.Dsl.Logging.Logger;

namespace ConDep.Dsl.LoadBalancer.AlohaHaProxy.Tests
{
    [TestFixture]
    public class HaProxyTests
    {
        class UnitTestLogger : Log4NetLoggerBase
        {
            private readonly MemoryAppender _memAppender;

            public UnitTestLogger(ILog log4netLog, MemoryAppender memAppender) : base(log4netLog)
            {
                _memAppender = memAppender;
            }

            public override void Warn(string message, object[] formatArgs)
            {
            }

            public override void Warn(string message, Exception ex, object[] formatArgs)
            {
            }

            public override void Verbose(string message, object[] formatArgs)
            {
            }

            public override void Verbose(string message, Exception ex, object[] formatArgs)
            {
            }

            public override void Info(string message, object[] formatArgs)
            {
            }

            public override void Info(string message, Exception ex, object[] formatArgs)
            {
            }

            public override void Error(string message, object[] formatArgs)
            {
            }

            public override void Error(string message, Exception ex, object[] formatArgs)
            {
            }

            public override void Progress(string message, params object[] formatArgs)
            {
            }

            public override void ProgressEnd()
            {
            }

            public override void LogSectionStart(string name)
            {
            }

            public override void LogSectionEnd(string name)
            {
            }

            public LoggingEvent[] Events { get { return _memAppender.GetEvents(); } }

            public void ClearEvents()
            {
                _memAppender.Clear();
            }
        }

        private LoadBalancerConfig _config;
        private FluentMockServer _server;

        private UnitTestLogger CreateMemoryLogger()
        {
            var memAppender = new MemoryAppender { Name = "MemoryAppender" };
            memAppender.ActivateOptions();

            var repo = LogManager.GetRepository() as Hierarchy;
            repo.Root.AddAppender(memAppender);
            repo.Configured = true;
            repo.RaiseConfigurationChanged(EventArgs.Empty);

            return new UnitTestLogger(LogManager.GetLogger("root"), memAppender);
        }

        [SetUp]
        public void Setup()
        {
            _server = FluentMockServer.Start();
            _config = new LoadBalancerConfig
            {

                Name = "http://localhost:" + _server.Port,
                Provider = "ConDep.Dsl.LoadBalancer.AlohaHaProxy.dll",
                UserName = "username",
                Password = "password",
                Mode = "Sticky"
            };
            _config.CustomConfig = new ExpandoObject();

            _config.CustomConfig.SnmpEndpoint = "endpointaddress";
            _config.CustomConfig.Scope = "root";
            _config.CustomConfig.SnmpPort = 161;
            _config.CustomConfig.SnmpCommunity = "public";
            _config.CustomConfig.WaitTimeInSecondsAfterSettingServerStateToOffline = 20;
            _config.CustomConfig.WaitTimeInSecondsAfterSettingServerStateToOnline = 2;
            Logger.Initialize(CreateMemoryLogger());
        }
        
        [Test]
        public void TestThat_StateCanBeFetchedForOffline()
        {
            _server
                .Given(
                    Requests.WithUrl("/api/2/scope/root/l7/farm/thefarm/server/theserver")
                )
                .RespondWith(
                    Responses
                        .WithStatusCode(200)
                        .WithBody(@"{
	""address"": ""0.0.0.0"",

            ""port"": ""0"",
            ""max_connections"": ""1000"",
            ""weight"": ""10"",
            ""http_cookie_id"": ""theserver"",
            ""sorry"": null,
            ""check"": ""enabled"",
            ""maintenance"": ""enabled"",
            ""ssl"": ""enabled""
        }")
                );
            var lb = new HaProxyLoadBalancer(_config);
            var result = lb.GetServerState("theserver", "thefarm");
            Assert.AreEqual(result, HaProxyLoadBalancer.ServerState.Offline);
        }

        [Test]
        public void TestThat_StateCanBeFetchedForOnline()
        {
            _server
                .Given(
                    Requests.WithUrl("/api/2/scope/root/l7/farm/thefarm/server/theserver")
                )
                .RespondWith(
                    Responses
                        .WithStatusCode(200)
                        .WithBody(@"{
	""address"": ""0.0.0.0"",

            ""port"": ""0"",
            ""max_connections"": ""1000"",
            ""weight"": ""10"",
            ""http_cookie_id"": ""theserver"",
            ""sorry"": null,
            ""check"": ""enabled"",
            ""maintenance"": null,
            ""ssl"": ""enabled""
        }")
                );
            var lb = new HaProxyLoadBalancer(_config);
            var result = lb.GetServerState("theserver", "thefarm");
            Assert.AreEqual(result, HaProxyLoadBalancer.ServerState.Online);
        }
    }
}
