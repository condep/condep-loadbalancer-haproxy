using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using ConDep.Dsl.Config;
using ConDep.Dsl.Logging;
using ConDep.Dsl.Operations.LoadBalancer;
using SnmpSharpNet;

namespace ConDep.Dsl.LoadBalancer.AlohaHaProxy
{
    public class HaProxyLoadBalancer : ILoadBalance
    {
        private readonly LoadBalancerConfig _config;
        private string _scope;
        private string _snmpEndpoint;
        private int _snmpPort;
        private string _snmpCommunity;

        private enum ServerState
        {
            Online,
            Offline
        }

        public HaProxyLoadBalancer(LoadBalancerConfig config)
        {
            _config = config;
            if (config.CustomConfig == null) throw new ConDepLoadBalancerException("No config found in section CustomConfig under load balancer. HaProxy requires CustomConfig for at least SnmpEndpoint.");

            if (config.CustomConfig.SnmpEndpoint != null)
            {
                _snmpEndpoint = config.CustomConfig.SnmpEndpoint;
            }
            else
            {
                throw new ConDepLoadBalancerException(
                    "No SNMP endpoint exist in configuration. Please add this under CustomConfig.");
            }

            _scope = config.CustomConfig.Scope ?? "root";
            _snmpPort = config.CustomConfig.SnmpPort ?? 161;
            _snmpCommunity = config.CustomConfig.SnmpCommunity ?? "public";
        }

        public void BringOffline(string serverName, string farm, LoadBalancerSuspendMethod suspendMethod, IReportStatus status)
        {
            var result = ChangeServerState(serverName, farm, ServerState.Offline);

            if (!result.IsSuccessStatusCode)
            {
                throw new ConDepLoadBalancerException(string.Format("Failed to take server {0} offline in loadbalancer. Returned status code was {1} with reason: {2}", serverName, result.StatusCode, result.ReasonPhrase));
            }

            WaitForCurrentConnectionsToDrain(farm, serverName, _snmpEndpoint, _snmpPort, _snmpCommunity, DateTime.Now.AddSeconds(_config.TimeoutInSeconds));
        }

        public void BringOnline(string serverName, string farm, IReportStatus status)
        {
            var result = ChangeServerState(serverName, farm, ServerState.Online);

            if (!result.IsSuccessStatusCode)
            {
                throw new ConDepLoadBalancerException(string.Format("Failed to take server {0} online in loadbalancer. Returned status code was {1} with reason: {2}", serverName, result.StatusCode, result.ReasonPhrase));
            }
        }

        private static string GetServerStateCommand(ServerState serverState)
        {
            switch (serverState)
            {
                case ServerState.Offline:
                    return "\"enabled\"";
                case ServerState.Online:
                    return "null";
            }
            throw new ConDepLoadBalancerException("Server state for command unknown");
        }

        private HttpResponseMessage ChangeServerState(string serverName, string farm, ServerState serverState)
        {
            var cmd = GetServerStateCommand(serverState);
            var client = new HttpClient {BaseAddress = new Uri(_config.Name)};
            var request = new HttpRequestMessage(HttpMethod.Put,
                _config.Name + "/api/2/scope/" + _scope + "/l7/farm/" + farm + "/server/" + serverName);
            request.Headers.Accept.Clear();
            request.Headers.Add("Authorization",
                "Basic " +
                Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", _config.UserName, _config.Password))));
            request.Content = new StringContent(string.Format("{{ \"maintenance\" : {0} }}", cmd), Encoding.ASCII, "application/json");
            var result = client.SendAsync(request).Result;
            return result;
        }

        private const string FARM_NAMES_OID          = ".1.3.6.1.4.1.23263.4.2.1.3.3.1.3";
        private const string SERVER_NAMES_OID        = ".1.3.6.1.4.1.23263.4.2.1.3.4.1.4.1.";
        private const string SERVER_CUR_SESSIONS_OID = ".1.3.6.1.4.1.23263.4.2.1.3.4.1.8.1.";


        private void WaitForCurrentConnectionsToDrain(string farm, string server, string snmpEndpoint, int snmpPort, string snmpCommunity, DateTime timeout)
        {
            if (DateTime.Now > timeout) throw new ConDepLoadBalancerException(string.Format("Timed out while taking {0} offline in load balancer.", server));

            var snmp = new SimpleSnmp(snmpEndpoint, snmpPort, snmpCommunity);
            var farmResult = snmp.Walk(SnmpVersion.Ver2, FARM_NAMES_OID);
            var farmOid = farmResult.Single(x => x.Value.Clone().ToString() == farm);

            var id = farmOid.Key.ToString();
            var start = farmOid.Key.ToString().LastIndexOf(".");
            var farmSubId = id.Substring(start + 1, id.Length - start - 1);

            var serverResult = snmp.Walk(SnmpVersion.Ver2, SERVER_NAMES_OID + farmSubId);
            var serverOid = serverResult.Single(x => x.Value.Clone().ToString() == server);

            var serverId = serverOid.Key.ToString();
            start = serverOid.Key.ToString().LastIndexOf(".");
            var serverSubId = serverId.Substring(start + 1, serverId.Length - start - 1);

            var pdu = Pdu.GetPdu();
            pdu.VbList.Add(SERVER_CUR_SESSIONS_OID + farmSubId + "." + serverSubId);
            var currentSessionsVal = snmp.Get(SnmpVersion.Ver2, pdu);
            var val = currentSessionsVal.Single().Value.Clone() as Counter64;
            if (val > 0)
            {
                var waitInterval = 3;
                Logger.Warn(string.Format("There are still {0} active connections on server {1}. Waiting {2} second before retry.", val, server, waitInterval));
                Thread.Sleep(1000 * waitInterval);
                WaitForCurrentConnectionsToDrain(farm, server, snmpEndpoint, snmpPort, snmpCommunity, timeout);
            }
        }

        public LbMode Mode { get; set; }
    }
}
