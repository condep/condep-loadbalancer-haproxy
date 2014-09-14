using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ConDep.Dsl.Config;
using NUnit.Framework;

namespace ConDep.Dsl.LoadBalancer.AlohaHaProxy.Tests
{
    [TestFixture]
    public class HaProxyTests
    {
        private string _json =
    @"{
    ""LoadBalancer"": 
    {
        ""Name"": ""jat-nlb01"",
        ""Provider"": ""ConDep.Dsl.LoadBalancer.AlohaHaProxy.dll"",
        ""UserName"": ""nlbUser"",
        ""Password"": ""verySecureP@ssw0rd"",
        ""Mode"": ""Sticky"",
		""SuspendMode"" : ""Graceful"",
        ""CustomConfig"" :
        {
            ""SnmpEndpoint"" : ""10.0.0.1""
        }
    },
	""Servers"":
    [
        {
            ""Name"" : ""jat-web01"",
            ""LoadBalancerFarm"": ""farm1"",
        },
        {
            ""Name"" : ""jat-web02"",
            ""LoadBalancerFarm"": ""farm1"",
        }
    ],
    ""DeploymentUser"": 
    {
        ""UserName"": ""torresdal\\condepuser"",
        ""Password"": ""verySecureP@ssw0rd""
    }
}";

        private ConDepEnvConfig _config;

        [SetUp]
        public void Setup()
        {
            var memStream = new MemoryStream(Encoding.UTF8.GetBytes(_json));

            var parser = new EnvConfigParser();
            _config = parser.GetTypedEnvConfig(memStream, null);
        }

        [Test]
        public void TestThat_ConfigurationCanBeLoaded()
        {
            var tmp = "";
        }
    }
}
