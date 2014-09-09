using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using NUnit.Framework;
using SnmpSharpNet;

namespace ConDep.Dsl.LoadBalancer.AlohaHaProxy.Tests
{
    [TestFixture]
    public class HaProxyTests
    {
        private string _user = "admin";
        private string _pass = "noldus,2000";
        private string _scope = "root";
        private string _uri = "http://z63os2spx001:4444";
        private string _farm = "preprod_frende_no";
        private string _serverName = "z63os2swb03-b";

        [Test]
        public void TestThat_CanGetServerInfo()
        {
            var client = new HttpClient {BaseAddress = new Uri(_uri)};
            var request = new HttpRequestMessage(HttpMethod.Put, _uri + "/api/2/scope/" + _scope + "/l7/farm/" + _farm + "/server/" + _serverName);
            request.Headers.Accept.Clear();
            request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", _user, _pass))));
            request.Content = new StringContent("{ \"maintenance\" : \"enabled\" }", Encoding.ASCII, "application/json");
            var result = client.SendAsync(request).Result;
            Assert.That(result.IsSuccessStatusCode);
        }
    }

    [TestFixture]
    public class SnmpTests
    {
        private string _farm = "preprod_frende_no";
        private string _server = "z63os2swb03-b";

        [Test]
        public void TestThat_CanPollCurrConnections()
        {
            var snmp = new SimpleSnmp("10.78.212.50", 161, "public");
            var farmResult = snmp.Walk(SnmpVersion.Ver2, ".1.3.6.1.4.1.23263.4.2.1.3.3.1.3");
            var farmOid = farmResult.Single(x => x.Value.Clone().ToString() == _farm);

            var id = farmOid.Key.ToString();
            var start = farmOid.Key.ToString().LastIndexOf(".");
            var farmSubId = id.Substring(start + 1, id.Length - start - 1);

            var serverResult = snmp.Walk(SnmpVersion.Ver2, ".1.3.6.1.4.1.23263.4.2.1.3.4.1.4.1." + farmSubId);
            var serverOid = serverResult.Single(x => x.Value.Clone().ToString() == _server);

            var serverId = serverOid.Key.ToString();
            start = serverOid.Key.ToString().LastIndexOf(".");
            var serverSubId = serverId.Substring(start + 1, serverId.Length - start - 1);

            var pdu = Pdu.GetPdu();
            pdu.VbList.Add(".1.3.6.1.4.1.23263.4.2.1.3.4.1.8.1." + farmSubId + "." + serverSubId);
            var currentSessionsVal = snmp.Get(SnmpVersion.Ver2, pdu);
            var val = currentSessionsVal.Single().Value.Clone() as Counter64;
            ulong tmp = val.Value;

        }
    }
}
